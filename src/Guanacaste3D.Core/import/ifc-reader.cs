using System.Numerics;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Imports the geometry of IFC (2x3 and 4) files: the way a Revit model reaches this application. Walls, slabs,
/// roofs, columns, doors, windows, stairs and furniture are rebuilt from their extrusions, faceted shells and
/// tessellations; instanced families are expanded; openings are cut out of their host elements; wall tops clipped by
/// roofs are cut by their half-spaces; and surface styles become colors and glass. Coordinates are converted to
/// meters, and survey-scale origins are brought back near zero so single precision keeps its accuracy.
/// </summary>
internal static class IfcReader
{
    private static readonly HashSet<string> ProductTypes =
    [
        "IFCWALL", "IFCWALLSTANDARDCASE", "IFCWALLELEMENTEDCASE", "IFCSLAB", "IFCSLABSTANDARDCASE", "IFCSLABELEMENTEDCASE",
        "IFCROOF", "IFCCOVERING", "IFCBEAM", "IFCBEAMSTANDARDCASE", "IFCCOLUMN", "IFCCOLUMNSTANDARDCASE", "IFCMEMBER",
        "IFCMEMBERSTANDARDCASE", "IFCPLATE", "IFCPLATESTANDARDCASE", "IFCDOOR", "IFCDOORSTANDARDCASE", "IFCWINDOW",
        "IFCWINDOWSTANDARDCASE", "IFCSTAIR", "IFCSTAIRFLIGHT", "IFCRAMP", "IFCRAMPFLIGHT", "IFCRAILING", "IFCCURTAINWALL",
        "IFCFOOTING", "IFCPILE", "IFCBUILDINGELEMENTPROXY", "IFCBUILDINGELEMENTPART", "IFCELEMENTASSEMBLY",
        "IFCFURNISHINGELEMENT", "IFCFURNITURE", "IFCSYSTEMFURNITUREELEMENT", "IFCSITE", "IFCGEOGRAPHICELEMENT",
        "IFCCIVILELEMENT", "IFCTRANSPORTELEMENT", "IFCDISCRETEACCESSORY", "IFCFASTENER", "IFCMECHANICALFASTENER",
        "IFCREINFORCINGBAR", "IFCREINFORCINGMESH", "IFCTENDON", "IFCSANITARYTERMINAL", "IFCLIGHTFIXTURE", "IFCFLOWTERMINAL",
        "IFCFLOWSEGMENT", "IFCFLOWFITTING", "IFCFLOWCONTROLLER", "IFCFLOWMOVINGDEVICE", "IFCFLOWSTORAGEDEVICE",
        "IFCFLOWTREATMENTDEVICE", "IFCENERGYCONVERSIONDEVICE", "IFCDISTRIBUTIONELEMENT", "IFCDISTRIBUTIONFLOWELEMENT",
        "IFCDISTRIBUTIONCONTROLELEMENT", "IFCELECTRICAPPLIANCE", "IFCELECTRICDISTRIBUTIONBOARD", "IFCBUILDINGELEMENT",
        "IFCSHADINGDEVICE", "IFCCHIMNEY", "IFCEARTHWORKSELEMENT", "IFCSOLARDEVICE", "IFCOUTLET", "IFCSENSOR", "IFCACTUATOR",
        "IFCALARM", "IFCPIPESEGMENT", "IFCPIPEFITTING", "IFCDUCTSEGMENT", "IFCDUCTFITTING", "IFCCABLESEGMENT", "IFCCABLECARRIERSEGMENT"
    ];

    // Never drawn: openings are only cutters, spaces are volumes, and the rest carry no body.
    private static readonly HashSet<string> NotDrawn = ["IFCOPENINGELEMENT", "IFCSPACE", "IFCVIRTUALELEMENT", "IFCANNOTATION", "IFCGRID"];

    public static ImportedModel Read(string path, ModelImportOptions options, IProgress<ImportProgress>? progress,
        CancellationToken cancellation)
    {
        progress?.Report(new ImportProgress(0.02, "Import.Reading"));
        var step = StepFile.Load(path, cancellation);
        var index = IndexFile(step, cancellation);
        if (!index.AnyIfc) throw new InvalidDataException("The IFC file is not valid.");
        progress?.Report(new ImportProgress(0.15, "Import.Reading"));

        var (lengthUnit, angleUnit) = ReadUnits(step, index.Project);
        var meters = options.MetersPerUnit ?? lengthUnit;
        var geometry = new IfcGeometry(step, meters, angleUnit);
        var styles = new StyleResolver(step, index.StyledItems);
        var mesh = new MeshAccumulator();

        var shift = ChooseShift(step, geometry, index.Products);
        if (shift != default) mesh.Warn("Import.Warning.IfcShifted");
        var openings = new OpeningCache(step, geometry, shift);
        var failed = 0;
        var drawn = 0;

        for (var i = 0; i < index.Products.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            if ((i & 0x3F) == 0)
                progress?.Report(new ImportProgress(0.15 + 0.8 * i / index.Products.Count, "Import.Reading"));
            var (id, type) = index.Products[i];
            try
            {
                if (EmitProduct(step, geometry, styles, openings, index, mesh, id, type, shift, options)) drawn++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not InvalidDataException)
            {
                failed++;
            }
        }
        if (failed > 0) mesh.Warn("Import.Warning.IfcElementsSkipped");
        if (geometry.Unsupported.Count > 0) mesh.Warn("Import.Warning.IfcUnsupported");
        if (mesh.TriangleCount == 0) throw new InvalidDataException("The model file contains no geometry.");
        return mesh.Build(path, "IFC", ModelFrame.ZUp, 1.0, options);
    }

    // ------------------------------------------------------------------ indexing

    private sealed record FileIndex(bool AnyIfc, int? Project, List<(int Id, string Type)> Products, List<int> StyledItems,
        Dictionary<int, List<int>> Voids);

    private static FileIndex IndexFile(StepFile step, CancellationToken cancellation)
    {
        var products = new List<(int, string)>();
        var styled = new List<int>();
        var voids = new Dictionary<int, List<int>>();
        var relations = new List<int>();
        int? project = null;
        var any = false;
        var counter = 0;
        foreach (var id in step.Ids)
        {
            if ((++counter & 0x3FFFF) == 0) cancellation.ThrowIfCancellationRequested();
            var type = step.TypeOf(id);
            if (type is null || !type.StartsWith("IFC", StringComparison.Ordinal)) continue;
            any = true;
            switch (type)
            {
                case "IFCPROJECT":
                    project ??= id;
                    break;
                case "IFCSTYLEDITEM":
                    styled.Add(id);
                    break;
                case "IFCRELVOIDSELEMENT":
                    relations.Add(id);
                    break;
                default:
                    if (ProductTypes.Contains(type) || type == "IFCOPENINGELEMENT") products.Add((id, type));
                    break;
            }
        }
        foreach (var relation in relations)
        {
            if (step.Get(relation, cache: false) is not { } entity) continue;
            if (entity.Ref(4) is not { } host || entity.Ref(5) is not { } opening) continue;
            if (!voids.TryGetValue(host.Id, out var list)) voids[host.Id] = list = [];
            list.Add(opening.Id);
        }
        return new FileIndex(any, project, products, styled, voids);
    }

    // ------------------------------------------------------------------ units

    /// <summary>Meters per length unit and radians per plane-angle unit declared by the project.</summary>
    private static (double Length, double Angle) ReadUnits(StepFile step, int? projectId)
    {
        double length = 1, angle = 1; // without a declared unit IFC angles are radians
        if (projectId is not { } id || step.Get(id) is not { } project || project.Ref(8) is not { } assignmentRef ||
            step.Get(assignmentRef.Id) is not { } assignment || assignment.List(0) is not { } units)
            return (length, angle);
        foreach (var unitRef in units.OfType<StepRef>())
        {
            if (step.Get(unitRef.Id) is not { } unit) continue;
            if (unit.Type == "IFCSIUNIT")
            {
                var factor = Prefix(unit.Enum(2));
                if (unit.Enum(1) == "LENGTHUNIT") length = factor;
                else if (unit.Enum(1) == "PLANEANGLEUNIT") angle = 1;
            }
            else if (unit.Type == "IFCCONVERSIONBASEDUNIT" && unit.Ref(3) is { } measureRef &&
                     step.Get(measureRef.Id) is { } measure)
            {
                var value = StepFile.AsNumber(measure.Args.Count > 0 ? measure.Args[0] : null) ?? 1;
                var baseScale = measure.Ref(1) is { } baseUnit && step.Get(baseUnit.Id) is { Type: "IFCSIUNIT" } si
                    ? Prefix(si.Enum(2)) : 1;
                if (unit.Enum(1) == "LENGTHUNIT") length = value * baseScale;
                else if (unit.Enum(1) == "PLANEANGLEUNIT") angle = value;
            }
        }
        return (length, angle);
    }

    private static double Prefix(string? prefix) => prefix switch
    {
        "EXA" => 1e18, "PETA" => 1e15, "TERA" => 1e12, "GIGA" => 1e9, "MEGA" => 1e6, "KILO" => 1e3, "HECTO" => 1e2,
        "DECA" => 10, "DECI" => 0.1, "CENTI" => 0.01, "MILLI" => 0.001, "MICRO" => 1e-6, "NANO" => 1e-9, _ => 1
    };

    // ------------------------------------------------------------------ origin

    /// <summary>
    /// Survey coordinates (hundreds of thousands of meters) cannot be held in single precision to the millimeter. When
    /// the model is far from the origin, the median product position becomes the new origin.
    /// </summary>
    private static D3 ChooseShift(StepFile step, IfcGeometry geometry, List<(int Id, string Type)> products)
    {
        var origins = new List<D3>();
        foreach (var (id, _) in products.Take(400))
            if (step.Get(id) is { } entity && entity.Ref(5) is { } placement)
                origins.Add(geometry.Placement(placement.Id).Origin);
        if (origins.Count == 0) return default;
        var far = origins.Any(o => Math.Abs(o.X) > 5000 || Math.Abs(o.Y) > 5000 || Math.Abs(o.Z) > 5000);
        if (!far) return default;
        return new D3(Median(origins.Select(o => o.X)), Median(origins.Select(o => o.Y)), Median(origins.Select(o => o.Z)));
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    // ------------------------------------------------------------------ products

    private static bool EmitProduct(StepFile step, IfcGeometry geometry, StyleResolver styles, OpeningCache openings,
        FileIndex index, MeshAccumulator mesh, int id, string type, D3 shift, ModelImportOptions options)
    {
        if (NotDrawn.Contains(type)) return false;
        if (step.Get(id) is not { } product || product.Ref(6) is not { } shapeRef ||
            step.Get(shapeRef.Id) is not { Type: "IFCPRODUCTDEFINITIONSHAPE" } shape) return false;

        var parts = new List<Part>();
        var tessellated = false;
        foreach (var representationId in RepresentationsOf(step, shape))
        {
            var representation = step.Get(representationId)!;
            if (representation.Text(2) == "Tessellation") tessellated = true;
            foreach (var item in representation.List(3)?.OfType<StepRef>() ?? [])
                parts.AddRange(geometry.Item(item.Id));
        }
        if (parts.Count == 0) return false;

        var placement = product.Ref(5) is { } placementRef ? geometry.Placement(placementRef.Id) : Xform.Identity;
        var matrix = placement.ToMatrix(shift);
        var worldParts = parts.Select(part => part with { Soup = part.Soup.Transformed(matrix) }).ToList();

        // Tessellated bodies (IFC4 reference view) already have their openings cut out.
        if (!tessellated && index.Voids.TryGetValue(id, out var cutters))
        {
            var result = new List<Part>();
            foreach (var part in worldParts)
            {
                var pieces = new List<TriSoup> { part.Soup };
                foreach (var cutterId in cutters)
                {
                    if (openings.For(cutterId) is not { } cutter) continue;
                    var (min, max) = part.Soup.Bounds();
                    if (!Overlaps(min, max, cutter.Min, cutter.Max)) continue;
                    var next = new List<TriSoup>();
                    foreach (var piece in pieces)
                    {
                        var (pieceMin, pieceMax) = piece.Bounds();
                        if (Overlaps(pieceMin, pieceMax, cutter.Min, cutter.Max))
                            next.AddRange(MeshClipper.Subtract(piece, cutter.Planes));
                        else next.Add(piece);
                    }
                    pieces = next;
                }
                result.AddRange(pieces.Select(piece => part with { Soup = piece }));
            }
            worldParts = result;
        }

        if (mesh.TriangleCount + worldParts.Sum(p => p.Soup.TriangleCount) > options.MaximumTriangles)
            throw new InvalidDataException("The model file is too large to import.");
        foreach (var part in worldParts)
        {
            var material = MaterialFor(mesh, styles.For(part.StyleItem), type);
            var points = part.Soup.Points;
            for (var t = 0; t + 2 < points.Count; t += 3)
            {
                var start = mesh.Positions.Count;
                mesh.Positions.Add(points[t]);
                mesh.Positions.Add(points[t + 1]);
                mesh.Positions.Add(points[t + 2]);
                mesh.AddTriangle(start, start + 1, start + 2, material);
            }
        }
        return true;
    }

    private static bool Overlaps(Vector3 minA, Vector3 maxA, Vector3 minB, Vector3 maxB) =>
        minA.X <= maxB.X && maxA.X >= minB.X && minA.Y <= maxB.Y && maxA.Y >= minB.Y && minA.Z <= maxB.Z && maxA.Z >= minB.Z;

    /// <summary>Selects the shape representations that carry the element's visible body.</summary>
    private static List<int> RepresentationsOf(StepFile step, StepEntity shape)
    {
        var all = (shape.List(2) ?? []).OfType<StepRef>().Select(r => r.Id)
            .Where(id => step.Get(id) is { Type: "IFCSHAPEREPRESENTATION" }).ToList();
        var body = all.Where(id => step.Get(id)!.Text(1) == "Body").ToList();
        if (body.Count > 0) return body;
        string[] solidTypes = ["SweptSolid", "Brep", "Tessellation", "SurfaceModel", "Clipping", "CSG", "MappedRepresentation", "AdvancedBrep"];
        return all.Where(id =>
            step.Get(id)!.Text(1) is not ("Axis" or "Box" or "FootPrint" or "Annotation" or "Curve2D" or "Clearance") &&
            solidTypes.Contains(step.Get(id)!.Text(2) ?? "")).ToList();
    }

    // ------------------------------------------------------------------ materials

    private static int MaterialFor(MeshAccumulator mesh, StyleInfo? style, string type)
    {
        if (style is { } s)
        {
            var key = $"ifc:{(int)(s.Color.X * 255)}:{(int)(s.Color.Y * 255)}:{(int)(s.Color.Z * 255)}:{(int)(s.Transparency * 100)}";
            return mesh.MaterialIndex(key, () => s.Transparency > 0.05f
                ? MaterialLibrary.ClearGlass with
                {
                    Name = "Vidrio IFC", Transmission = Math.Clamp(s.Transparency * 1.15f, 0.35f, 0.97f),
                    TransmissionTint = Vector3.Lerp(new Vector3(0.9f, 0.95f, 0.97f), ToLinear(s.Color), 0.3f)
                }
                : new RenderMaterial { Name = "IFC", BaseColor = ToLinear(s.Color), Roughness = 0.7f });
        }
        return mesh.MaterialIndex("ifcdefault:" + type, () => DefaultMaterial(type));
    }

    private static Vector3 ToLinear(Vector3 srgb) => new(MaterialLibrary.SrgbToLinear(srgb.X),
        MaterialLibrary.SrgbToLinear(srgb.Y), MaterialLibrary.SrgbToLinear(srgb.Z));

    private static RenderMaterial DefaultMaterial(string type)
    {
        RenderMaterial Matte(string name, float r, float g, float b, float roughness = 0.85f, SurfacePattern pattern = SurfacePattern.None) =>
            new() { Name = name, BaseColor = ToLinear(new Vector3(r, g, b)), Roughness = roughness, Pattern = pattern };
        return type switch
        {
            "IFCWALL" or "IFCWALLSTANDARDCASE" or "IFCWALLELEMENTEDCASE" or "IFCCURTAINWALL" => Matte("Muro", 0.86f, 0.84f, 0.80f, 0.92f, SurfacePattern.Concrete),
            "IFCSLAB" or "IFCSLABSTANDARDCASE" or "IFCSLABELEMENTEDCASE" or "IFCFOOTING" => Matte("Losa", 0.66f, 0.65f, 0.62f, 0.8f, SurfacePattern.Concrete),
            "IFCROOF" => Matte("Techo", 0.55f, 0.28f, 0.22f, 0.7f),
            "IFCCOVERING" => Matte("Acabado", 0.92f, 0.92f, 0.90f, 0.9f),
            "IFCWINDOW" or "IFCWINDOWSTANDARDCASE" => MaterialLibrary.ClearGlass with { Name = "Vidrio" },
            "IFCDOOR" or "IFCDOORSTANDARDCASE" => Matte("Madera", 0.55f, 0.36f, 0.2f, 0.5f, SurfacePattern.Planks),
            "IFCBEAM" or "IFCBEAMSTANDARDCASE" or "IFCCOLUMN" or "IFCCOLUMNSTANDARDCASE" or "IFCMEMBER" or "IFCMEMBERSTANDARDCASE"
                or "IFCPILE" => Matte("Estructura", 0.62f, 0.61f, 0.58f, 0.8f, SurfacePattern.Concrete),
            "IFCRAILING" => new RenderMaterial { Name = "Metal", BaseColor = ToLinear(new Vector3(0.3f, 0.3f, 0.32f)), Roughness = 0.4f, Metallic = 0.8f },
            "IFCSITE" or "IFCGEOGRAPHICELEMENT" or "IFCEARTHWORKSELEMENT" => MaterialLibrary.ForKind(Geometry.SurfaceKind.Ground),
            _ => Matte("IFC", 0.75f, 0.75f, 0.73f)
        };
    }

    // ------------------------------------------------------------------ styles

    private readonly record struct StyleInfo(Vector3 Color, float Transparency);

    /// <summary>Resolves the surface style (color and transparency) that colors a representation item.</summary>
    private sealed class StyleResolver
    {
        private readonly StepFile _step;
        private readonly Dictionary<int, StyleInfo?> _byItem = [];
        private readonly Dictionary<int, StyleInfo?> _bySurface = [];

        public StyleResolver(StepFile step, List<int> styledItems)
        {
            _step = step;
            foreach (var id in styledItems)
            {
                if (step.Get(id, cache: false) is not { } styled || styled.Ref(0) is not { } item) continue;
                if (_byItem.ContainsKey(item.Id)) continue;
                foreach (var styleRef in styled.List(1)?.OfType<StepRef>() ?? [])
                {
                    var style = Resolve(styleRef.Id, 0);
                    if (style is null) continue;
                    _byItem[item.Id] = style;
                    break;
                }
            }
        }

        /// <summary>The style of an item, falling back to the first operand of a boolean (where Revit attaches it).</summary>
        public StyleInfo? For(int itemId)
        {
            for (var depth = 0; depth < 6; depth++)
            {
                if (_byItem.TryGetValue(itemId, out var style)) return style;
                if (_step.Get(itemId, cache: false) is not { } item) return null;
                if (item.Type is "IFCBOOLEANCLIPPINGRESULT" or "IFCBOOLEANRESULT" && item.Ref(1) is { } first) itemId = first.Id;
                else return null;
            }
            return null;
        }

        private StyleInfo? Resolve(int id, int depth)
        {
            if (depth > 4) return null;
            if (_bySurface.TryGetValue(id, out var cached)) return cached;
            StyleInfo? result = null;
            if (_step.Get(id, cache: false) is { } entity)
            {
                switch (entity.Type)
                {
                    case "IFCPRESENTATIONSTYLEASSIGNMENT" or "IFCSURFACESTYLE":
                        foreach (var child in entity.List(entity.Type == "IFCSURFACESTYLE" ? 2 : 0)?.OfType<StepRef>() ?? [])
                            if ((result = Resolve(child.Id, depth + 1)) is not null) break;
                        break;
                    case "IFCSURFACESTYLERENDERING" or "IFCSURFACESTYLESHADING":
                        result = Colour(entity);
                        break;
                }
            }
            return _bySurface[id] = result;
        }

        private StyleInfo? Colour(StepEntity rendering)
        {
            if (rendering.Ref(0) is not { } colourRef || _step.Get(colourRef.Id, cache: false) is not { Type: "IFCCOLOURRGB" } colour)
                return null;
            var color = new Vector3((float)colour.Number(1), (float)colour.Number(2), (float)colour.Number(3));
            // A diffuse factor (a plain ratio) scales the surface colour; a diffuse colour replaces it.
            if (rendering.Type == "IFCSURFACESTYLERENDERING" && rendering.Args.Count > 2)
            {
                if (rendering.Args[2] is StepTyped { Value: double ratio }) color *= (float)Math.Clamp(ratio, 0, 1);
                else if (rendering.Args[2] is StepRef diffuseRef && _step.Get(diffuseRef.Id, cache: false) is { Type: "IFCCOLOURRGB" } diffuse)
                    color = new Vector3((float)diffuse.Number(1), (float)diffuse.Number(2), (float)diffuse.Number(3));
            }
            var transparency = (float)Math.Clamp(rendering.Number(1), 0, 1);
            return new StyleInfo(Vector3.Clamp(color, Vector3.Zero, Vector3.One), transparency);
        }
    }

    // ------------------------------------------------------------------ openings

    private sealed record Cutter(List<(Vector3 Normal, float D)> Planes, Vector3 Min, Vector3 Max);

    /// <summary>Builds each opening's cutter once: its exact planes when convex, its bounding box otherwise.</summary>
    private sealed class OpeningCache(StepFile step, IfcGeometry geometry, D3 shift)
    {
        private readonly Dictionary<int, Cutter?> _cutters = [];

        public Cutter? For(int id)
        {
            if (_cutters.TryGetValue(id, out var cached)) return cached;
            return _cutters[id] = Build(id);
        }

        private Cutter? Build(int id)
        {
            if (step.Get(id) is not { } opening || opening.Ref(6) is not { } shapeRef ||
                step.Get(shapeRef.Id) is not { Type: "IFCPRODUCTDEFINITIONSHAPE" } shape) return null;
            var placement = opening.Ref(5) is { } p ? geometry.Placement(p.Id) : Xform.Identity;
            var matrix = placement.ToMatrix(shift);
            var combined = new TriSoup();
            foreach (var representationId in RepresentationsOf(step, shape))
                foreach (var item in step.Get(representationId)!.List(3)?.OfType<StepRef>() ?? [])
                    foreach (var part in geometry.Item(item.Id))
                        combined.Append(part.Soup.Transformed(matrix));
            if (combined.TriangleCount == 0) return null;
            var (min, max) = combined.Bounds();
            var planes = MeshClipper.ConvexPlanes(combined);
            if (planes is null)
            {
                // Not convex (or not closed): cut with its bounding box instead.
                planes =
                [
                    (Vector3.UnitX, max.X), (-Vector3.UnitX, -min.X), (Vector3.UnitY, max.Y), (-Vector3.UnitY, -min.Y),
                    (Vector3.UnitZ, max.Z), (-Vector3.UnitZ, -min.Z)
                ];
            }
            return new Cutter(planes, min, max);
        }
    }
}
