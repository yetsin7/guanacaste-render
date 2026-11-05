using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Guanacaste3D.RevitBridge;

/// <summary>
/// Writes a Revit document as a Guanacaste exchange file (.g3dx): JSON with the levels, the materials, the triangle
/// meshes and every model element with the BIM data Guanacaste needs to rebuild it natively (wall centerlines, hosted
/// door and window positions, slab outlines, rooms). Lengths are meters, Z up, in Revit's internal coordinates.
/// Identical family geometry is written once: an instance refers to a mesh in its own frame (origin plus a turn about Z).
/// The format is documented in docs/revit-import.md.
/// </summary>
internal sealed partial class ExchangeExporter
{
    public const int FormatVersion = 1;
    private const double Meters = 0.3048;

    private readonly Document _document;
    private readonly View3D? _view;
    private readonly Options _options = new() { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
    private readonly Dictionary<long, int> _materialIndex = [];
    private readonly List<JsonObject> _materials = [];
    private readonly Dictionary<ulong, int> _meshByHash = [];
    private readonly List<MeshBuilder> _meshes = [];
    private readonly JsonArray _elements = [];
    private readonly Dictionary<string, int> _skipped = new(StringComparer.Ordinal);

    /// <summary>Exports the whole model, or with <paramref name="view"/> only what that 3D view shows (hidden
    /// categories, filters, section box and phase applied).</summary>
    public ExchangeExporter(Document document, View3D? view = null)
    {
        _document = document;
        _view = view;
    }

    public int ElementCount => _elements.Count;
    public int TriangleCount => _meshes.Sum(mesh => mesh.TriangleCount);

    /// <summary>Reads the whole model and writes the exchange file, reporting progress as a fraction.</summary>
    public void Export(string path, Action<double>? progress = null)
    {
        var levels = new FilteredElementCollector(_document).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(level => level.ProjectElevation).ToList();
        var collector = _view is null ? new FilteredElementCollector(_document) : new FilteredElementCollector(_document, _view.Id);
        var candidates = collector.WhereElementIsNotElementType()
            .Where(IsModelElement).ToList();
        for (var index = 0; index < candidates.Count; index++)
        {
            try
            {
                Visit(candidates[index]);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or InvalidOperationException
                                           or ArgumentException or NullReferenceException)
            {
                Count("error:" + ex.GetType().Name);
            }
            if (index % 50 == 0) progress?.Invoke((double)index / Math.Max(1, candidates.Count));
        }
        foreach (var room in new FilteredElementCollector(_document).OfCategory(BuiltInCategory.OST_Rooms)
                     .WhereElementIsNotElementType().Cast<SpatialElement>())
            VisitRoom(room);
        Write(path, levels);
    }

    /// <summary>Elements with model geometry, leaving out datums, annotation, imported CAD, demolished and secondary options.</summary>
    private bool IsModelElement(Element element)
    {
        if (element.Category is not { CategoryType: CategoryType.Model } category) return false;
        if (element is ImportInstance or RevitLinkInstance or SpatialElement) return false;
        if (element.ViewSpecific) return false;
        var builtIn = (BuiltInCategory)category.Id.Value;
        if (builtIn is BuiltInCategory.OST_Lines or BuiltInCategory.OST_Cameras or BuiltInCategory.OST_Mass
            or BuiltInCategory.OST_SectionBox or BuiltInCategory.OST_Levels or BuiltInCategory.OST_Grids
            or BuiltInCategory.OST_Rooms or BuiltInCategory.OST_Areas or BuiltInCategory.OST_RvtLinks
            or BuiltInCategory.OST_LightingFixtureSource or BuiltInCategory.OST_MEPSpaces
            or BuiltInCategory.OST_StairsRuns or BuiltInCategory.OST_StairsLandings
            or BuiltInCategory.OST_StairsSupports)
            return false;
        if (element.DesignOption is { IsPrimary: false }) return false;
        if (element.DemolishedPhaseId is { } demolished && demolished != ElementId.InvalidElementId) return false;
        return true;
    }

    /// <summary>Creates an exchange element while sharing repeated meshes and preserving host metadata.</summary>
    private void Visit(Element element)
    {
        var record = new JsonObject
        {
            ["id"] = element.Id.Value,
            ["uniqueId"] = element.UniqueId,
            ["category"] = element.Category is { } category ? ((BuiltInCategory)category.Id.Value).ToString() : "",
            ["categoryName"] = element.Category?.Name ?? "",
            ["name"] = element.Name ?? ""
        };
        var type = _document.GetElement(element.GetTypeId()) as ElementType;
        record["family"] = type?.FamilyName ?? "";
        record["type"] = type?.Name ?? "";
        if (LevelOf(element) is { } level) record["level"] = level.Value;
        if (element.LookupParameter("Mark") is { HasValue: true } mark) record["mark"] = mark.AsString() ?? "";

        // The instance frame lets identical family geometry be shared; everything else is written in model coordinates.
        var (origin, rotation) = FrameOf(element);
        var builder = new MeshBuilder(origin, rotation);
        if (element.get_Geometry(_options) is { } geometry) Collect(geometry, builder, element);
        if (builder.TriangleCount == 0 && element is not Wall)
        {
            Count("empty:" + record["category"]);
            return;
        }
        if (builder.TriangleCount > 0)
        {
            record["mesh"] = Share(builder);
            record["origin"] = new JsonArray(origin.X * Meters, origin.Y * Meters, origin.Z * Meters);
            record["rotation"] = rotation;
        }

        switch (element)
        {
            case Wall wall:
                record["kind"] = "wall";
                record["wall"] = DescribeWall(wall);
                break;
            case FamilyInstance { Host: Wall host } instance
                when instance.Category?.Id.Value is (long)BuiltInCategory.OST_Doors or (long)BuiltInCategory.OST_Windows:
                record["kind"] = instance.Category.Id.Value == (long)BuiltInCategory.OST_Doors ? "door" : "window";
                record["opening"] = DescribeOpening(instance, host);
                break;
            case Floor or Ceiling or RoofBase:
                record["kind"] = element switch { Floor => "floor", Ceiling => "ceiling", _ => "roof" };
                if (DescribeSlab((HostObject)element) is { } slab) record["slab"] = slab;
                break;
            case FamilyInstance:
                record["kind"] = "instance";
                break;
            default:
                record["kind"] = "mesh";
                break;
        }
        _elements.Add(record);
    }

    /// <summary>Insertion point and turn about Z of an upright family instance; the model origin for everything else.</summary>
    private static (XYZ Origin, double Rotation) FrameOf(Element element)
    {
        if (element is not FamilyInstance instance) return (XYZ.Zero, 0);
        var transform = instance.GetTotalTransform();
        var axis = new XYZ(transform.BasisX.X, transform.BasisX.Y, 0);
        if (Math.Abs(transform.BasisZ.Z) < 0.999 || axis.GetLength() < 1e-6) return (transform.Origin, 0);
        return (transform.Origin, Math.Atan2(axis.Y, axis.X));
    }

    /// <summary>Adds the triangles of solids, meshes and nested instances, in model coordinates.</summary>
    private void Collect(GeometryElement geometry, MeshBuilder builder, Element owner)
    {
        foreach (var item in geometry)
        {
            switch (item)
            {
                case Solid { Faces.Size: > 0 } solid:
                    foreach (Face face in solid.Faces)
                    {
                        Mesh mesh;
                        try { mesh = face.Triangulate(0.35); }
                        catch (Autodesk.Revit.Exceptions.ApplicationException) { continue; }
                        if (mesh is null) continue;
                        AddMesh(mesh, builder, MaterialOf(face.MaterialElementId, owner));
                    }
                    break;
                case Mesh mesh:
                    AddMesh(mesh, builder, MaterialOf(mesh.MaterialElementId, owner));
                    break;
                case GeometryInstance instance when instance.GetInstanceGeometry() is { } nested:
                    Collect(nested, builder, owner);
                    break;
            }
        }
    }

    /// <summary>Appends a tessellated Revit face with indices offset to the shared mesh vertex buffer.</summary>
    private static void AddMesh(Mesh mesh, MeshBuilder builder, int material)
    {
        var start = builder.VertexCount;
        foreach (XYZ vertex in mesh.Vertices) builder.AddVertex(vertex);
        for (var index = 0; index < mesh.NumTriangles; index++)
        {
            var triangle = mesh.get_Triangle(index);
            builder.AddTriangle(start + (int)triangle.get_Index(0), start + (int)triangle.get_Index(1),
                start + (int)triangle.get_Index(2), material);
        }
    }

    /// <summary>Index of a face's material, falling back to the element's category material.</summary>
    private int MaterialOf(ElementId id, Element owner)
    {
        if (id == ElementId.InvalidElementId && owner.Category?.Material is { } categoryMaterial) id = categoryMaterial.Id;
        if (_materialIndex.TryGetValue(id.Value, out var index)) return index;
        var material = _document.GetElement(id) as Material;
        var color = material?.Color is { IsValid: true } valid ? valid : null;
        var entry = new JsonObject
        {
            ["name"] = material?.Name ?? owner.Category?.Name ?? "Default",
            ["class"] = material?.MaterialClass ?? "",
            ["category"] = material?.MaterialCategory ?? "",
            ["color"] = color is null ? "#B4B4B4" : $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}",
            ["transparency"] = (material?.Transparency ?? 0) / 100.0,
            ["shininess"] = (material?.Shininess ?? 0) / 128.0,
            ["ownerCategory"] = owner.Category is { } category ? ((BuiltInCategory)category.Id.Value).ToString() : ""
        };
        index = _materials.Count;
        _materials.Add(entry);
        _materialIndex[id.Value] = index;
        return index;
    }

    /// <summary>Stores a mesh once per distinct geometry and returns its index.</summary>
    private int Share(MeshBuilder builder)
    {
        var hash = builder.Hash();
        if (_meshByHash.TryGetValue(hash, out var existing)) return existing;
        _meshes.Add(builder);
        return _meshByHash[hash] = _meshes.Count - 1;
    }
}
