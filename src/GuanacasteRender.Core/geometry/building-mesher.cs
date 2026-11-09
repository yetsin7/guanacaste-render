using System.Numerics;

namespace Guanacaste3D.Core.Geometry;

/// <summary>Physical surface categories that renderers map to visual materials.</summary>
public enum SurfaceKind : byte
{
    Wall,
    Floor,
    Glass,
    Ground,
    DoorLeaf,
    OpeningFrame,
    Structure,
    /// <summary>Leaves of trees and shrubs.</summary>
    Foliage,
    /// <summary>Trunks and limbs of trees.</summary>
    Bark,
    /// <summary>Soil exposed on the sides and underside of a toposolid.</summary>
    Earth,
    /// <summary>The weathering surface of a pitched roof (tiles by default).</summary>
    Roof,
    /// <summary>Single leaves of render-quality vegetation: thin, translucent to light from behind.</summary>
    Leaf,
    /// <summary>The faces of walls that look into the building (plastered by default); <see cref="Wall"/> is the outside.</summary>
    InteriorWall,
    /// <summary>Ironmongery: handles, hinges and roses, in brushed steel.</summary>
    Metal,
    /// <summary>The glowing diffuser or shade of a light fixture; it lights the scene in the render.</summary>
    LightEmitter,
    /// <summary>Upholstery fabric: sofas, armchairs, duvets and rugs.</summary>
    Upholstery,
    /// <summary>Bed linen: mattresses and pillows.</summary>
    Bedding,
    /// <summary>Glazed vitreous china: toilets, basins, bathtubs and shower trays.</summary>
    Ceramic,
    /// <summary>The diffuser of a light fixture that is switched off or has no power.</summary>
    LightOff,
    /// <summary>The small, bright lens of a downlight: about as much light as a ceiling fixture from a tenth of the area.</summary>
    SpotEmitter,
    /// <summary>Exposed clay brick facing.</summary>
    WallBrick,
    /// <summary>Exposed concrete block facing.</summary>
    WallBlock,
    /// <summary>Fair-faced concrete.</summary>
    WallConcrete,
    /// <summary>Natural stone facing.</summary>
    WallStone,
    /// <summary>Timber cladding.</summary>
    WallWood,
    /// <summary>Metal panel cladding.</summary>
    WallMetal,
    /// <summary>Silvered glass: bathroom and wall mirrors.</summary>
    Mirror,
    /// <summary>Cold-water pipe, blue as in Revit's domestic cold water system.</summary>
    PipeCold,
    /// <summary>Hot-water pipe, red.</summary>
    PipeHot,
    /// <summary>PVC sanitary drain.</summary>
    PipeDrain,
    /// <summary>Rainwater drain, green as in Revit's storm system.</summary>
    PipeStorm,
    /// <summary>The glass face of a photovoltaic module: deep blue cells under smooth glass.</summary>
    SolarCell,
    /// <summary>Fire-protection red enamel: sprinkler mains, extinguishers, hose cabinets and alarm devices.</summary>
    FireRed,
    /// <summary>Still water in a pool or reflecting basin: dark, glossy, and it mirrors the sky and what stands beside it.</summary>
    Water,
    /// <summary>Glossy clear-coated car paint, deep navy.</summary>
    CarPaint,
    /// <summary>Metallic silver car paint.</summary>
    CarPaintSilver,
    /// <summary>Tyres and rubber trim.</summary>
    Rubber,
    /// <summary>Asphalt road surfacing.</summary>
    Asphalt,
    /// <summary>Dark tinted glass, glossy and opaque: car windows of models without an interior.</summary>
    TintedGlass,
    /// <summary>The sea of the render: it takes the place of the endless ground, with waves and white caps.</summary>
    Ocean,
    /// <summary>Boulders and stones of the landscape: grey, rough, mottled rock.</summary>
    Rock
}

/// <summary>A texture a mesh maps by coordinates, and the coverage below which its surface is cut away.</summary>
public sealed record SceneTexture(Rendering.RenderTexture Texture, float AlphaCutoff, Rendering.NormalMap? Normal = null);

/// <summary>The texture coordinates of a triangle's three corners and the <see cref="SceneMesh.Textures"/> entry it uses.</summary>
public readonly record struct TexturedTriangle(Vector2 A, Vector2 B, Vector2 C, int Texture);

/// <summary>Contiguous triangle range attributed to one editable BIM element.</summary>
public readonly record struct MeshElementRange(int First, int End, Guid ElementId);

/// <summary>An indexed triangle soup in Z-up model coordinates (meters).</summary>
public sealed class SceneMesh
{
    public List<Vector3> Positions { get; } = [];
    public List<int> Indices { get; } = [];
    public List<SurfaceKind> TriangleKinds { get; } = [];
    /// <summary>Sorted non-overlapping ranges; unowned triangles inherit global appearance.</summary>
    public List<MeshElementRange> ElementRanges { get; } = [];

    /// <summary>Associates newly appended triangles with one element, with constant metadata per range.</summary>
    public void MarkElement(Guid elementId, int first)
    {
        if (elementId == Guid.Empty || first < 0 || first > TriangleCount)
            throw new ArgumentOutOfRangeException(nameof(first));
        if (first < TriangleCount) ElementRanges.Add(new MeshElementRange(first, TriangleCount, elementId));
    }

    /// <summary>Textures that triangles of this mesh map with their own coordinates (detailed family meshes).</summary>
    public List<SceneTexture> Textures { get; } = [];

    /// <summary>
    /// The triangles that carry texture coordinates, by triangle index, with the texture (in <see cref="Textures"/>) they
    /// map. Sparse, so the many ways geometry is appended never have to keep a parallel list aligned.
    /// </summary>
    public Dictionary<int, TexturedTriangle> TexturedTriangles { get; } = [];

    /// <summary>
    /// Per-vertex shading normals, aligned with <see cref="Positions"/>, or null when every triangle is flat. A zero
    /// normal marks a flat vertex. Created by the first <see cref="AddSmoothTriangle"/>.
    /// </summary>
    public List<Vector3>? Normals { get; private set; }

    public int TriangleCount => TriangleKinds.Count;

    /// <summary>
    /// Triangle ranges [First, End) that belong to building services (electrical devices, plumbing, air conditioning) rather
    /// than to the architecture, so a view can show them through walls and floors.
    /// </summary>
    public List<(int First, int End)> Services { get; } = [];

    /// <summary>Whether a triangle belongs to a building service.</summary>
    public bool IsService(int triangle) => Services.Any(range => triangle >= range.First && triangle < range.End);

    /// <summary>
    /// Per-triangle output of emitting surfaces, for lights on a dimmer: 1 for full output. Null until a dimmed light is
    /// added, and shorter than <see cref="TriangleCount"/> when the triangles after it are at full output.
    /// </summary>
    public List<float>? EmissionScale { get; private set; }

    /// <summary>The output of one triangle's emission, 1 unless it belongs to a dimmed light.</summary>
    public float EmissionOf(int triangle) => EmissionScale is { } scale && triangle < scale.Count ? scale[triangle] : 1;

    /// <summary>Scales the emission of every triangle added since <paramref name="firstTriangle"/>.</summary>
    public void Dim(int firstTriangle, double output)
    {
        if (output >= 1) return;
        EmissionScale ??= [];
        while (EmissionScale.Count < TriangleCount) EmissionScale.Add(1);
        for (var triangle = firstTriangle; triangle < TriangleCount; triangle++) EmissionScale[triangle] = (float)Math.Max(0, output);
    }

    /// <summary>Appends one triangle with its own three vertices.</summary>
    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, SurfaceKind kind)
    {
        var start = Positions.Count;
        if (Normals is { } normals)
        {
            normals.Add(Vector3.Zero);
            normals.Add(Vector3.Zero);
            normals.Add(Vector3.Zero);
        }
        Positions.Add(a);
        Positions.Add(b);
        Positions.Add(c);
        Indices.Add(start);
        Indices.Add(start + 1);
        Indices.Add(start + 2);
        TriangleKinds.Add(kind);
    }

    /// <summary>Appends a triangle shaded with interpolated vertex normals, for curved surfaces such as foliage.</summary>
    public void AddSmoothTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normalA, Vector3 normalB, Vector3 normalC,
        SurfaceKind kind)
    {
        if (Normals is null)
        {
            Normals = new List<Vector3>(Positions.Count + 3);
            for (var index = 0; index < Positions.Count; index++) Normals.Add(Vector3.Zero);
        }
        AddTriangle(a, b, c, kind);
        var start = Positions.Count - 3;
        Normals[start] = normalA;
        Normals[start + 1] = normalB;
        Normals[start + 2] = normalC;
    }

    /// <summary>Appends a planar quad given in counter-clockwise order seen from its front side.</summary>
    public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, SurfaceKind kind)
    {
        AddTriangle(a, b, c, kind);
        AddTriangle(a, c, d, kind);
    }

    /// <summary>Returns the axis-aligned bounds of all vertices, or a unit box when empty.</summary>
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        if (Positions.Count == 0) return (Vector3.Zero, Vector3.One);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var position in Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        return (min, max);
    }
}

/// <summary>Converts the BIM document into renderer-neutral triangle geometry.</summary>
public static class BuildingMesher
{
    /// <summary>Builds closed solids for walls (split around openings), slabs, and glazing panes.</summary>
    public static SceneMesh Build(BuildingDocument document, bool includeGround = false, PlantDetail plants = PlantDetail.Model)
    {
        ArgumentNullException.ThrowIfNull(document);
        var elevations = document.Levels.ToDictionary(level => level.Id, level => level.Elevation);
        var mesh = new SceneMesh();
        var typesById = document.OpeningTypes.ToDictionary(type => type.Id);
        var joins = WallJoinIndex.Build(document.Walls);

        var insides = document.Levels.ToDictionary(level => level.Id, level => InteriorTest(document, level.Id));
        var wallTypes = document.WallTypes.ToDictionary(type => type.Id);
        foreach (var wall in document.Walls)
        {
            var z = elevations[wall.LevelId];
            var openings = document.Openings.Where(opening => opening.WallId == wall.Id).ToArray();
            var inside = insides[wall.LevelId];
            var wallType = wallTypes.GetValueOrDefault(wall.WallTypeId);
            var construction = wallType?.Construction ?? WallConstruction.Plaster;
            var facade = construction switch
            {
                WallConstruction.Brick => SurfaceKind.WallBrick,
                WallConstruction.Block => SurfaceKind.WallBlock,
                WallConstruction.Concrete => SurfaceKind.WallConcrete,
                WallConstruction.Stone => SurfaceKind.WallStone,
                WallConstruction.Wood => SurfaceKind.WallWood,
                WallConstruction.Metal => SurfaceKind.WallMetal,
                _ => SurfaceKind.Wall
            };
            // An interior partition of brick, block or concrete is usually left exposed on both faces.
            var exposedInside = wallType?.Function == WallFunction.Interior &&
                                construction is WallConstruction.Brick or WallConstruction.Block or WallConstruction.Concrete;
            // Like the layers of a wall type: a face that looks into the building is plaster, one that looks out is facade.
            SurfaceKind Face(Vector2 middle, Vector2 outward) =>
                inside(new Point2(middle.X + outward.X * 0.3, middle.Y + outward.Y * 0.3))
                    ? exposedInside ? facade : SurfaceKind.InteriorWall
                    : facade;
            var wallFirst = mesh.TriangleCount;
            foreach (var solid in WallGeometry.Solids(wall, openings))
            {
                if (construction == WallConstruction.Glass)
                {
                    AddGlassWall(mesh, wall, solid.From, solid.To, z + solid.Bottom, z + solid.Top);
                    continue;
                }
                var outline = joins.SectionOutline(wall, solid.From, solid.To).Select(point => new Vector2((float)point.X, (float)point.Y)).ToArray();
                if (!solid.Sloped)
                {
                    AddPrism(mesh, outline, (float)(z + solid.Bottom), (float)(z + solid.Top), facade, Face);
                    continue;
                }
                // A wedge: the top at each corner is the height of the slope above that corner's place along the wall.
                var axisX = (wall.End.X - wall.Start.X) / wall.Length;
                var axisY = (wall.End.Y - wall.Start.Y) / wall.Length;
                float TopAt(Vector2 corner) => (float)(z + Math.Max(solid.Bottom + 0.001,
                    solid.TopHeightAt((corner.X - wall.Start.X) * axisX + (corner.Y - wall.Start.Y) * axisY)));
                AddPrism(mesh, outline, (float)(z + solid.Bottom), (float)(z + solid.Top), facade, Face, TopAt);
            }
            mesh.MarkElement(wall.Id, wallFirst);
            foreach (var opening in openings)
            {
                if (!typesById.TryGetValue(opening.OpeningTypeId, out var type)) continue;
                var openingFirst = mesh.TriangleCount;
                AddOpeningAssembly(mesh, wall, opening, type, z);
                if (type.SecurityBars) AddSecurityBars(mesh, wall, opening, z, inside);
                mesh.MarkElement(opening.Id, openingFirst);
            }
        }

        // The ground plane meets the lowest corner of the site, so sloped terrain rises out of it like a real site.
        double? siteEdge = null;
        foreach (var floor in document.Floors)
        {
            var floorFirst = mesh.TriangleCount;
            var slabTop = elevations[floor.LevelId] + floor.ElevationOffset;
            if (floor.IsToposolid)
            {
                TopographyGeometry.AppendTo(mesh, floor, elevations[floor.LevelId]);
                var surface = new TopoSurface(floor);
                foreach (var corner in floor.Boundary)
                {
                    var z = elevations[floor.LevelId] + surface.ElevationAt(corner.X, corner.Y);
                    siteEdge = siteEdge is { } edge ? Math.Min(edge, z) : z;
                }
                mesh.MarkElement(floor.Id, floorFirst);
                continue;
            }
            if (floor.IsPitched)
            {
                var built = RoofGeometry.WithOverhang(floor);
                AddPitchedRoof(mesh, built, elevations[floor.LevelId] + built.ElevationOffset);
                if (floor.GableWalls) AddGableWalls(mesh, floor, slabTop);
            }
            else
            {
                AddSlab(mesh, RoofGeometry.WithOverhang(floor).Boundary, slabTop, floor.Thickness,
                    FloorOpenings(document, floor, elevations),
                    SlabSurface(floor));
            }
            mesh.MarkElement(floor.Id, floorFirst);
        }

        foreach (var column in document.Columns)
        {
            var first = mesh.TriangleCount;
            AddColumn(mesh, column, elevations[column.LevelId], document.Levels);
            mesh.MarkElement(column.Id, first);
        }

        // The electrical simulation decides which fixtures glow: switches, dimmers, breakers and panels.
        var electrical = ElectricalAnalysis.Analyze(document);
        var servicesFrom = mesh.TriangleCount;
        foreach (var device in document.ElectricalDevices)
        {
            var first = mesh.TriangleCount;
            var brightness = device.IsLuminaire ? electrical.Brightness(device.Id) : 0;
            ElectricalGeometry.AppendTo(mesh, device, elevations[device.LevelId], brightness);
            if (brightness > 0) mesh.Dim(first, brightness);
            mesh.MarkElement(device.Id, first);
        }

        // Plumbing follows the hydraulic layout: pipes sized and routed by the analysis.
        var buriedFrom = mesh.Positions.Count;
        PlumbingGeometry.AppendTo(mesh, document, PlumbingAnalysis.Analyze(document));
        var buriedTo = mesh.Positions.Count;
        mesh.Services.Add((servicesFrom, mesh.TriangleCount));

        foreach (var element in document.StructuralElements.Where(element => element.HasSolid))
        {
            var elementFirst = mesh.TriangleCount;
            var datum = elevations[element.LevelId];
            if (ComponentFamilyRules.MeshOf(document, element) is { } familyMesh)
            {
                var planting = element.Kind == StructuralElementKind.Planting;
                var ground = planting ? TerrainUnder(document, element.LevelId, element.Start) : 0;
                // A small render-only embed hides gaps at uneven ground and mesh bases.
                if (planting && plants == PlantDetail.Render) ground -= 0.08;
                // A plant's width is its canopy spread: both plan axes scale alike, so the crown keeps its shape.
                var size = familyMesh.Decode().Size;
                var spread = element.Width / Math.Max(size.X, size.Y);
                familyMesh.AppendTo(mesh, element.Start, element.Rotation,
                    planting ? size.X * spread : element.Width,
                    planting ? size.Y * spread : element.Depth > 0 ? element.Depth : element.Width,
                    element.Height, datum + ground + element.Offset, preview: plants == PlantDetail.Model);
                mesh.MarkElement(element.Id, elementFirst);
                continue;
            }
            if (element.Kind == StructuralElementKind.Planting)
            {
                // Like a Revit site component, a plant stands on the toposolid it is placed on.
                var ground = datum + TerrainUnder(document, element.LevelId, element.Start);
                if (plants == PlantDetail.Render) ground -= 0.08;
                if (plants == PlantDetail.Render) PlantDetailGeometry.AppendTo(mesh, element, ground);
                else PlantGeometry.AppendTo(mesh, element, ground);
                mesh.MarkElement(element.Id, elementFirst);
                continue;
            }
            var output = element.Kind == StructuralElementKind.Component ? electrical.Brightness(element.Id) : 1;
            var first = mesh.TriangleCount;
            foreach (var part in ElementGeometry.Parts(element))
                AddPart(mesh, output > 0 || part.Kind != SurfaceKind.LightEmitter ? part : part with { Kind = SurfaceKind.LightOff }, datum);
            if (output > 0) mesh.Dim(first, output);
            mesh.MarkElement(element.Id, elementFirst);
        }

        foreach (var family in document.InPlaceFamilies)
        {
            var first = mesh.TriangleCount;
            FamilyGeometry.AppendTo(mesh, family);
            mesh.MarkElement(family.Id, first);
        }

        if (includeGround)
        {
            var (min, max) = mesh.Bounds();
            var center = (min + max) / 2;
            // Large enough that the ground meets the sky at the horizon from typical orbit distances.
            var extent = MathF.Max(500, (max - min).Length() * 20);
            // Pipes are buried below grade: they must not drag the ground down with them and lift the house out of it.
            var lowest = float.MaxValue;
            for (var index = 0; index < mesh.Positions.Count; index++)
                if (index < buriedFrom || index >= buriedTo) lowest = MathF.Min(lowest, mesh.Positions[index].Z);
            if (lowest == float.MaxValue) lowest = 0;
            var z = siteEdge is { } site ? (float)site - 0.02f : MathF.Min(0, lowest) - 0.01f;
            mesh.AddQuad(
                new(center.X - extent, center.Y - extent, z), new(center.X + extent, center.Y - extent, z),
                new(center.X + extent, center.Y + extent, z), new(center.X - extent, center.Y + extent, z),
                SurfaceKind.Ground);
        }
        return mesh;
    }

    /// <summary>
    /// Whether a plan point of a level lies inside the building: it is enclosed by walls, meaning a ray cast from it in
    /// each of eight directions meets a wall of the level. A terrace or a courtyard open on one side is outside; door
    /// and window openings do not let the rays out, because walls are tested along their whole length.
    /// </summary>
    private static Func<Point2, bool> InteriorTest(BuildingDocument document, Guid levelId)
    {
        var walls = document.Walls.Where(wall => wall.LevelId == levelId).Select(wall => (wall.Start, wall.End)).ToArray();
        if (walls.Length < 3) return _ => false;
        var directions = Enumerable.Range(0, 8).Select(index => (Math.Cos(index * Math.PI / 4 + 0.1), Math.Sin(index * Math.PI / 4 + 0.1))).ToArray();
        return point => directions.All(direction => walls.Any(wall => Hits(point, direction, wall.Start, wall.End)));

        static bool Hits(Point2 origin, (double X, double Y) direction, Point2 a, Point2 b)
        {
            var ex = b.X - a.X;
            var ey = b.Y - a.Y;
            var denominator = direction.X * ey - direction.Y * ex;
            if (Math.Abs(denominator) < 1e-12) return false;
            var ox = a.X - origin.X;
            var oy = a.Y - origin.Y;
            var t = (ox * ey - oy * ex) / denominator;
            var u = (ox * direction.Y - oy * direction.X) / denominator;
            return t > 0 && t < 60 && u >= -1e-6 && u <= 1 + 1e-6;
        }
    }

    /// <summary>Elevation, above its level, of the toposolid surface under a plan point; zero where there is none.</summary>
    public static double TerrainUnder(BuildingDocument document, Guid levelId, Point2 point)
    {
        foreach (var floor in document.Floors)
            if (floor.IsToposolid && floor.LevelId == levelId && TopographyGeometry.Contains(floor.Boundary, point))
                return new TopoSurface(floor).ElevationAt(point.X, point.Y);
        return 0;
    }

    /// <summary>
    /// Builds a hosted opening in layers, like a detailed family: a lining (mocheta) recessed inside
    /// the rough opening, then a door leaf with stiles, rails and a recessed panel, or a window sash
    /// with glass and a projecting sill. Each member is its own solid so its profile reads on screen.
    /// </summary>
    private static void AddOpeningAssembly(SceneMesh mesh, Wall wall, WallOpening opening, OpeningType type,
        double elevation)
    {
        // A wall opening is a plain hole: no lining, sash, leaf or sill.
        if (type.Operation == OpeningOperation.Void) return;
        var bottom = elevation + opening.Sill;
        var top = bottom + opening.Height;
        var lining = Math.Min(type.FrameWidth, Math.Min(opening.Width, opening.Height) / 6);
        // The lining sits 1 cm inside both wall faces, so the reveal reads as a step rather than a flush cut.
        var liningHalf = wall.Thickness / 2 - Math.Min(0.01, wall.Thickness / 4);
        AddWallBlock(mesh, wall, opening.Begin, opening.Begin + lining, -liningHalf, liningHalf, bottom, top,
            SurfaceKind.OpeningFrame);
        AddWallBlock(mesh, wall, opening.End - lining, opening.End, -liningHalf, liningHalf, bottom, top,
            SurfaceKind.OpeningFrame);
        AddWallBlock(mesh, wall, opening.Begin + lining, opening.End - lining, -liningHalf, liningHalf,
            top - lining, top, SurfaceKind.OpeningFrame);
        if (opening.Kind == OpeningKind.Window)
        {
            if (type.Operation is OpeningOperation.Standard or OpeningOperation.Casement or OpeningOperation.Awning)
                AddWindowSash(mesh, wall, opening, type, lining, bottom, top);
            else
                AddOperatedWindow(mesh, wall, opening, type, lining, bottom, top);
            if (type.Operation is OpeningOperation.Casement or OpeningOperation.Awning)
                AddWindowHandles(mesh, wall, opening, type, lining, bottom, top);
            AddWindowSill(mesh, wall, opening, bottom);
            return;
        }
        var depth = type.Operation == OpeningOperation.Revolving ? type.Depth : WallGeometry.LeafDepth(type, wall);
        if (depth <= 0) return;
        if (type.Operation == OpeningOperation.Standard)
        {
            AddDoorLeaves(mesh, wall, opening, depth, lining, bottom, top, type.GlazedLeaf, type.LeafStyle);
            AddDoorHardware(mesh, wall, opening, type, depth, lining, bottom);
        }
        else
        {
            AddOperatedDoor(mesh, wall, opening, type, depth, lining, bottom, top);
        }
    }

    /// <summary>
    /// Adds the leaves of a door that does not swing: sliding panels on two tracks, a closed pocket leaf, narrow
    /// folding leaves, overhead sections, or the glass wings of a revolving door.
    /// </summary>
    private static void AddOperatedDoor(SceneMesh mesh, Wall wall, WallOpening opening, OpeningType type, double depth,
        double lining, double bottom, double top)
    {
        const double clearance = 0.004;
        const double thresholdGap = 0.012;
        var left = opening.Begin + lining + clearance;
        var right = opening.End - lining - clearance;
        var low = bottom + thresholdGap;
        var high = top - lining - clearance;
        var panelKind = type.GlazedLeaf ? SurfaceKind.Glass : SurfaceKind.DoorLeaf;
        switch (type.Operation)
        {
            case OpeningOperation.Sliding:
            {
                // Two panels overlap by one stile and run on separate tracks, one in front of the other.
                const double overlap = 0.05;
                var panelWidth = (right - left + overlap) / 2;
                var half = Math.Min(depth, 0.06) / 2;
                AddWallBlock(mesh, wall, left, right, -half * 2, half * 2, high, high + lining, SurfaceKind.OpeningFrame);
                for (var panel = 0; panel < 2; panel++)
                {
                    var from = panel == 0 ? left : right - panelWidth;
                    var shift = panel == 0 ? -half : half;
                    AddSlidingPanel(mesh, wall, from, from + panelWidth, shift - half, shift + half, low, high, panelKind);
                }
                break;
            }
            case OpeningOperation.Pocket:
                AddDoorPanel(mesh, wall, left, right, Math.Min(depth, 0.035), low, high);
                break;
            case OpeningOperation.Folding:
            {
                var leaves = Math.Max(2, (int)Math.Round((right - left) / 0.45 / 2) * 2);
                var leafWidth = (right - left) / leaves;
                for (var leaf = 0; leaf < leaves; leaf++)
                {
                    var from = left + leaf * leafWidth;
                    var shift = leaf % 2 == 0 ? -0.012 : 0.012;
                    AddWallBlock(mesh, wall, from + 0.002, from + leafWidth - 0.002, shift - 0.015, shift + 0.015,
                        low, high, SurfaceKind.DoorLeaf);
                }
                break;
            }
            case OpeningOperation.Overhead:
            {
                var sections = Math.Max(4, (int)Math.Round((high - low) / 0.5));
                var sectionHeight = (high - low) / sections;
                var half = Math.Min(depth, wall.Thickness) / 2;
                for (var section = 0; section < sections; section++)
                {
                    var from = low + section * sectionHeight;
                    AddWallBlock(mesh, wall, left, right, -half, half, from + 0.005, from + sectionHeight - 0.005,
                        SurfaceKind.DoorLeaf);
                }
                break;
            }
            case OpeningOperation.Revolving:
            {
                var center = (opening.Begin + opening.End) / 2;
                var radius = Math.Min((right - left) / 2, depth / 2);
                AddWallBlock(mesh, wall, center - radius, center + radius, -0.02, 0.02, low, high, SurfaceKind.Glass);
                AddWallBlock(mesh, wall, center - 0.02, center + 0.02, -radius, radius, low, high, SurfaceKind.Glass);
                AddWallBlock(mesh, wall, center - 0.05, center + 0.05, -0.05, 0.05, low, high, SurfaceKind.OpeningFrame);
                break;
            }
        }
    }

    /// <summary>One sliding panel: a frame of four members around a pane that is glass or timber.</summary>
    private static void AddSlidingPanel(SceneMesh mesh, Wall wall, double from, double to, double sideA, double sideB,
        double bottom, double top, SurfaceKind pane)
    {
        const double member = 0.05;
        AddWallBlock(mesh, wall, from, from + member, sideA, sideB, bottom, top, SurfaceKind.OpeningFrame);
        AddWallBlock(mesh, wall, to - member, to, sideA, sideB, bottom, top, SurfaceKind.OpeningFrame);
        AddWallBlock(mesh, wall, from + member, to - member, sideA, sideB, top - member, top, SurfaceKind.OpeningFrame);
        AddWallBlock(mesh, wall, from + member, to - member, sideA, sideB, bottom, bottom + member, SurfaceKind.OpeningFrame);
        var middle = (sideA + sideB) / 2;
        AddWallBlock(mesh, wall, from + member, to - member, middle - 0.003, middle + 0.003, bottom + member, top - member, pane);
    }

    /// <summary>Adds the sashes of sliding, hung and louvered windows inside the lining.</summary>
    private static void AddOperatedWindow(SceneMesh mesh, Wall wall, WallOpening opening, OpeningType type,
        double lining, double bottom, double top)
    {
        var member = Math.Min(type.FrameWidth, 0.07);
        const double sashHalf = 0.02;
        const double glassHalf = 0.003;
        const double plane = 0.0225;
        var from = opening.Begin + lining;
        var to = opening.End - lining;
        var sashBottom = bottom + lining;
        var sashTop = top - lining;
        var columns = type.Operation == OpeningOperation.Sliding ? Math.Max(2, type.GlazingLightCount) : 1;
        var rows = type.Operation == OpeningOperation.Hung ? 2 : 1;
        var cellWidth = (to - from) / columns;
        var cellHeight = (sashTop - sashBottom) / rows;
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var x0 = from + column * cellWidth;
            var x1 = x0 + cellWidth;
            var y0 = sashBottom + row * cellHeight;
            var y1 = y0 + cellHeight;
            // Neighbouring sashes sit on alternate tracks so they can pass one another.
            var shift = type.Operation == OpeningOperation.Louvered ? 0 : ((row + column) % 2 == 0 ? -plane : plane);
            AddWallBlock(mesh, wall, x0, x0 + member, shift - sashHalf, shift + sashHalf, y0, y1, SurfaceKind.OpeningFrame);
            AddWallBlock(mesh, wall, x1 - member, x1, shift - sashHalf, shift + sashHalf, y0, y1, SurfaceKind.OpeningFrame);
            AddWallBlock(mesh, wall, x0 + member, x1 - member, shift - sashHalf, shift + sashHalf, y1 - member, y1,
                SurfaceKind.OpeningFrame);
            AddWallBlock(mesh, wall, x0 + member, x1 - member, shift - sashHalf, shift + sashHalf, y0, y0 + member,
                SurfaceKind.OpeningFrame);
            if (type.Operation == OpeningOperation.Louvered)
            {
                // Overlapping glass blades, each a thin horizontal slat.
                const double pitch = 0.07;
                const double blade = 0.045;
                for (var level = y0 + member; level + blade <= y1 - member + 1e-9; level += pitch)
                    AddWallBlock(mesh, wall, x0 + member, x1 - member, -0.035, 0.035, level, level + 0.006, SurfaceKind.Glass);
            }
            else
            {
                AddWallBlock(mesh, wall, x0 + member, x1 - member, shift - glassHalf, shift + glassHalf,
                    y0 + member, y1 - member, SurfaceKind.Glass);
            }
        }
    }

    /// <summary>Adds an operating handle on the free stile of each casement light, or at the base of an awning sash.</summary>
    private static void AddWindowHandles(SceneMesh mesh, Wall wall, WallOpening opening, OpeningType type, double lining,
        double bottom, double top)
    {
        var member = Math.Min(type.FrameWidth, 0.07);
        var from = opening.Begin + lining;
        var to = opening.End - lining;
        var lights = type.GlazingLightCount > 0 ? type.GlazingLightCount : opening.Width > 1.0 ? 2 : 1;
        var lightWidth = (to - from) / lights;
        if (type.Operation == OpeningOperation.Awning)
        {
            var center = (from + to) / 2;
            AddWallBlock(mesh, wall, center - 0.04, center + 0.04, 0.02, 0.04, bottom + lining + member / 2 - 0.01,
                bottom + lining + member / 2 + 0.01, SurfaceKind.OpeningFrame);
            return;
        }
        var handleHeight = bottom + (top - bottom) / 2;
        for (var light = 0; light < lights; light++)
        {
            // Handles alternate sides so adjacent casements meet at their handles.
            var edge = light % 2 == 0 ? from + (light + 1) * lightWidth - member : from + light * lightWidth + member;
            AddWallBlock(mesh, wall, edge - 0.01, edge + 0.01, 0.02, 0.04, handleHeight - 0.06, handleHeight + 0.06,
                SurfaceKind.OpeningFrame);
        }
    }

    /// <summary>Adds the door leaves, one per half of a wide opening, each clear of the lining by a few millimeters.</summary>
    private static void AddDoorLeaves(SceneMesh mesh, Wall wall, WallOpening opening, double depth, double lining,
        double bottom, double top, bool glazed = false, DoorLeafStyle style = DoorLeafStyle.Panelled)
    {
        const double clearance = 0.004;
        const double thresholdGap = 0.012;
        var leafCount = opening.Width > 1.2 ? 2 : 1;
        var left = opening.Begin + lining + clearance;
        var right = opening.End - lining - clearance;
        var leafWidth = (right - left) / leafCount;
        for (var leaf = 0; leaf < leafCount; leaf++)
        {
            var from = left + leaf * leafWidth;
            AddDoorLeaf(mesh, wall, from, from + leafWidth, depth, bottom + thresholdGap, top - clearance, glazed, style);
        }
    }

    /// <summary>One door leaf in the style of its type.</summary>
    private static void AddDoorLeaf(SceneMesh mesh, Wall wall, double from, double to, double depth, double bottom, double top,
        bool glazed, DoorLeafStyle style)
    {
        var half = depth / 2;
        switch (style)
        {
            case DoorLeafStyle.Flush:
                // A hollow-core flush leaf: one smooth slab with hardwood lipping at its edges.
                AddWallBlock(mesh, wall, from + 0.012, to - 0.012, -half, half, bottom, top - 0.012, SurfaceKind.DoorLeaf);
                AddWallBlock(mesh, wall, from, from + 0.012, -half, half, bottom, top, SurfaceKind.DoorLeaf);
                AddWallBlock(mesh, wall, to - 0.012, to, -half, half, bottom, top, SurfaceKind.DoorLeaf);
                AddWallBlock(mesh, wall, from + 0.012, to - 0.012, -half, half, top - 0.012, top, SurfaceKind.DoorLeaf);
                return;
            case DoorLeafStyle.SixPanel:
            {
                const double stile = 0.11;
                AddWallBlock(mesh, wall, from, from + stile, -half, half, bottom, top, SurfaceKind.DoorLeaf);
                AddWallBlock(mesh, wall, to - stile, to, -half, half, bottom, top, SurfaceKind.DoorLeaf);
                var middle = (from + to) / 2;
                AddWallBlock(mesh, wall, middle - 0.05, middle + 0.05, -half, half, bottom, top, SurfaceKind.DoorLeaf);
                // Rows: tall bottom pair, middle pair, short top pair, as on a colonial door.
                var heights = new[] { bottom, bottom + 0.20, bottom + 0.95, bottom + 1.12, top - 0.55, top - 0.40, top - 0.10, top };
                for (var index = 0; index < heights.Length; index += 2)
                    AddWallBlock(mesh, wall, from + stile, to - stile, -half, half, heights[index], heights[index + 1], SurfaceKind.DoorLeaf);
                for (var row = 1; row < heights.Length - 1; row += 2)
                    foreach (var (left, right) in new[] { (from + stile, middle - 0.05), (middle + 0.05, to - stile) })
                    {
                        AddWallBlock(mesh, wall, left, right, -half * 0.35, half * 0.35, heights[row], heights[row + 1], SurfaceKind.DoorLeaf);
                        if (right - left > 0.1 && heights[row + 1] - heights[row] > 0.1)
                            AddWallBlock(mesh, wall, left + 0.035, right - 0.035, -half * 0.7, half * 0.7,
                                heights[row] + 0.035, heights[row + 1] - 0.035, SurfaceKind.DoorLeaf);
                    }
                return;
            }
            case DoorLeafStyle.Louvered:
            {
                const double stile = 0.07;
                AddWallBlock(mesh, wall, from, from + stile, -half, half, bottom, top, SurfaceKind.DoorLeaf);
                AddWallBlock(mesh, wall, to - stile, to, -half, half, bottom, top, SurfaceKind.DoorLeaf);
                AddWallBlock(mesh, wall, from + stile, to - stile, -half, half, top - 0.08, top, SurfaceKind.DoorLeaf);
                AddWallBlock(mesh, wall, from + stile, to - stile, -half, half, bottom, bottom + 0.10, SurfaceKind.DoorLeaf);
                // Slats at 45°, drawn as two overlapping strips stepped through the leaf's depth.
                for (var z = bottom + 0.12; z < top - 0.10; z += 0.045)
                {
                    AddWallBlock(mesh, wall, from + stile, to - stile, -half * 0.9, 0, z, z + 0.022, SurfaceKind.DoorLeaf);
                    AddWallBlock(mesh, wall, from + stile, to - stile, 0, half * 0.9, z + 0.016, z + 0.038, SurfaceKind.DoorLeaf);
                }
                return;
            }
            case DoorLeafStyle.FullGlass:
                AddWallBlock(mesh, wall, from, to, -0.006, 0.006, bottom + 0.01, top - 0.01, SurfaceKind.Glass);
                // Patch fittings at the pivot corners.
                foreach (var z in new[] { bottom, top - 0.07 })
                    AddWallBlock(mesh, wall, from, from + 0.16, -0.012, 0.012, z, z + 0.07, SurfaceKind.Metal);
                return;
            case DoorLeafStyle.Security:
            {
                const double member = 0.05;
                AddWallBlock(mesh, wall, from, from + member, -half, half, bottom, top, SurfaceKind.OpeningFrame);
                AddWallBlock(mesh, wall, to - member, to, -half, half, bottom, top, SurfaceKind.OpeningFrame);
                AddWallBlock(mesh, wall, from + member, to - member, -half, half, top - member, top, SurfaceKind.OpeningFrame);
                AddWallBlock(mesh, wall, from + member, to - member, -half, half, bottom, bottom + 0.35, SurfaceKind.OpeningFrame);
                AddWallBlock(mesh, wall, from + member, to - member, -0.003, 0.003, bottom + 0.35, top - member, SurfaceKind.Glass);
                // A grille of square bars over the glass, on the outside face.
                for (var x = from + member + 0.1; x < to - member - 0.05; x += 0.1)
                    AddWallBlock(mesh, wall, x - 0.008, x + 0.008, half - 0.016, half, bottom + 0.35, top - member, SurfaceKind.OpeningFrame);
                foreach (var z in new[] { bottom + 0.9, bottom + 1.5 })
                    AddWallBlock(mesh, wall, from + member, to - member, half - 0.02, half, z, z + 0.03, SurfaceKind.OpeningFrame);
                return;
            }
            default:
                AddDoorPanel(mesh, wall, from, to, depth, bottom, top, glazed);
                return;
        }
    }

    /// <summary>
    /// A glass partition between two stations of its wall: a tempered pane in aluminium head and sill channels, with a
    /// mullion at least every 1.2 m.
    /// </summary>
    private static void AddGlassWall(SceneMesh mesh, Wall wall, double from, double to, double bottom, double top)
    {
        var half = Math.Max(0.025, wall.Thickness / 2);
        const double channel = 0.05;
        AddWallBlock(mesh, wall, from, to, -half, half, bottom, bottom + channel, SurfaceKind.Metal);
        AddWallBlock(mesh, wall, from, to, -half, half, top - channel, top, SurfaceKind.Metal);
        AddWallBlock(mesh, wall, from, to, -0.006, 0.006, bottom + channel, top - channel, SurfaceKind.Glass);
        var bays = Math.Max(1, (int)Math.Ceiling((to - from) / 1.2));
        for (var index = 0; index <= bays; index++)
        {
            var at = from + (to - from) * index / bays;
            var left = Math.Max(from, at - 0.025);
            var right = Math.Min(to, at + 0.025);
            AddWallBlock(mesh, wall, left, right, -half, half, bottom + channel, top - channel, SurfaceKind.Metal);
        }
    }

    /// <summary>
    /// A window grille on the outside face: a flat-bar frame and square bars every 12 cm, as fitted to ground-floor
    /// windows across Central America. The outside is the side away from the building's interior.
    /// </summary>
    private static void AddSecurityBars(SceneMesh mesh, Wall wall, WallOpening opening, double elevation, Func<Point2, bool> inside)
    {
        var length = Math.Max(wall.Length, 1e-9);
        var normalX = -(wall.End.Y - wall.Start.Y) / length;
        var normalY = (wall.End.X - wall.Start.X) / length;
        var middle = (opening.Begin + opening.End) / 2;
        var center = new Point2(wall.Start.X + (wall.End.X - wall.Start.X) / length * middle,
            wall.Start.Y + (wall.End.Y - wall.Start.Y) / length * middle);
        var side = inside(new Point2(center.X + normalX * 0.5, center.Y + normalY * 0.5)) ? -1 : 1;
        var face = side * (wall.Thickness / 2 + 0.03);
        var (near, far) = side > 0 ? (face - 0.016, face) : (face, face + 0.016);
        var bottom = elevation + opening.Sill;
        var top = bottom + opening.Height;
        AddWallBlock(mesh, wall, opening.Begin, opening.End, near, far, bottom, bottom + 0.03, SurfaceKind.OpeningFrame);
        AddWallBlock(mesh, wall, opening.Begin, opening.End, near, far, top - 0.03, top, SurfaceKind.OpeningFrame);
        AddWallBlock(mesh, wall, opening.Begin, opening.End, near, far, (bottom + top) / 2 - 0.015, (bottom + top) / 2 + 0.015, SurfaceKind.OpeningFrame);
        for (var at = opening.Begin; at <= opening.End + 1e-9; at += Math.Max(0.1, (opening.End - opening.Begin) / Math.Round((opening.End - opening.Begin) / 0.12)))
            AddWallBlock(mesh, wall, Math.Max(opening.Begin, at - 0.008), Math.Min(opening.End, at + 0.008), near, far, bottom, top, SurfaceKind.OpeningFrame);
    }

    /// <summary>
    /// Adds one door leaf the way a joiner builds a panelled door: full-depth stiles, a top rail, a lock rail at handle
    /// height and a deep bottom rail, framing two raised-and-fielded panels (a thin field with a thicker raised centre
    /// inside a bevel), or glass above the lock rail for a glazed leaf.
    /// </summary>
    private static void AddDoorPanel(SceneMesh mesh, Wall wall, double from, double to, double depth,
        double bottom, double top, bool glazed = false)
    {
        const double stile = 0.11;
        const double topRail = 0.11;
        const double lockRail = 0.16;
        const double bottomRail = 0.22;
        const double bevel = 0.04;
        var half = depth / 2;
        var kind = SurfaceKind.DoorLeaf;
        var lockBottom = Math.Min(bottom + 0.88, top - topRail - 0.4);
        AddWallBlock(mesh, wall, from, from + stile, -half, half, bottom, top, kind);
        AddWallBlock(mesh, wall, to - stile, to, -half, half, bottom, top, kind);
        AddWallBlock(mesh, wall, from + stile, to - stile, -half, half, top - topRail, top, kind);
        AddWallBlock(mesh, wall, from + stile, to - stile, -half, half, lockBottom, lockBottom + lockRail, kind);
        AddWallBlock(mesh, wall, from + stile, to - stile, -half, half, bottom, bottom + bottomRail, kind);
        foreach (var (low, high, isUpper) in new[] { (bottom + bottomRail, lockBottom, false), (lockBottom + lockRail, top - topRail, true) })
        {
            if (high - low < 0.05) continue;
            if (glazed && isUpper)
            {
                AddWallBlock(mesh, wall, from + stile, to - stile, -0.004, 0.004, low, high, SurfaceKind.Glass);
                continue;
            }
            // Field: thin, set back in the frame. Raised centre: thicker, inset by the bevel all round.
            AddWallBlock(mesh, wall, from + stile, to - stile, -half * 0.35, half * 0.35, low, high, kind);
            if (to - from - 2 * (stile + bevel) > 0.05 && high - low - 2 * bevel > 0.05)
                AddWallBlock(mesh, wall, from + stile + bevel, to - stile - bevel, -half * 0.7, half * 0.7,
                    low + bevel, high - bevel, kind);
        }
    }

    /// <summary>
    /// Ironmongery of a swing door, in brushed steel: a lever handle on a round rose on both faces at 95 cm, near the
    /// latch edge, and three butt hinges on the hinge edge. Double doors get a handle on each meeting stile.
    /// </summary>
    private static void AddDoorHardware(SceneMesh mesh, Wall wall, WallOpening opening, OpeningType type, double depth,
        double lining, double bottom)
    {
        if (type.Operation != OpeningOperation.Standard || depth <= 0) return;
        var left = opening.Begin + lining;
        var right = opening.End - lining;
        var half = depth / 2;
        var handleHeight = bottom + 0.95;
        var latches = opening.Width > 1.2
            ? new[] { ((left + right) / 2 - 0.07, -1.0), ((left + right) / 2 + 0.07, 1.0) }
            : new[] { opening.FlipHand ? (left + 0.065, 1.0) : (right - 0.065, -1.0) };
        foreach (var (at, toward) in latches)
            foreach (var face in new[] { -1.0, 1.0 })
            {
                var plate = face * half;
                // Rose: a short square-ish boss, then the lever running toward the hinge side.
                AddWallBlock(mesh, wall, at - 0.026, at + 0.026, Math.Min(plate, plate + face * 0.01), Math.Max(plate, plate + face * 0.01),
                    handleHeight - 0.026, handleHeight + 0.026, SurfaceKind.Metal);
                AddWallBlock(mesh, wall, at - 0.009, at + 0.009, Math.Min(plate, plate + face * 0.055), Math.Max(plate, plate + face * 0.055),
                    handleHeight - 0.009, handleHeight + 0.009, SurfaceKind.Metal);
                var leverFrom = Math.Min(at, at + toward * 0.13);
                var leverTo = Math.Max(at, at + toward * 0.13);
                AddWallBlock(mesh, wall, leverFrom, leverTo, Math.Min(plate + face * 0.045, plate + face * 0.065),
                    Math.Max(plate + face * 0.045, plate + face * 0.065), handleHeight - 0.01, handleHeight + 0.01, SurfaceKind.Metal);
            }
        if (opening.Width > 1.2) return;
        var hinge = opening.FlipHand ? right - 0.004 : left + 0.004;
        foreach (var height in new[] { bottom + 0.25, bottom + 1.05, bottom + opening.Height - 0.25 })
            AddWallBlock(mesh, wall, hinge - 0.006, hinge + 0.006, -half * 0.9, half * 0.9, height - 0.05, height + 0.05, SurfaceKind.Metal);
    }

    /// <summary>
    /// Adds the sash frame and glass inside the lining. A wide window is split into two lights by a
    /// mullion, the way a sliding window carries two sashes.
    /// </summary>
    private static void AddWindowSash(SceneMesh mesh, Wall wall, WallOpening opening, OpeningType type, double lining,
        double bottom, double top)
    {
        var member = Math.Min(type.FrameWidth, 0.07);
        const double sashHalf = 0.025;
        const double glassHalf = 0.003;
        var from = opening.Begin + lining;
        var to = opening.End - lining;
        var sashBottom = bottom + lining;
        var sashTop = top - lining;
        var lights = type.GlazingLightCount > 0 ? type.GlazingLightCount : opening.Width > 1.0 ? 2 : 1;
        var lightWidth = (to - from) / lights;
        for (var light = 0; light < lights; light++)
        {
            var lightFrom = from + light * lightWidth;
            var lightTo = lightFrom + lightWidth;
            AddWallBlock(mesh, wall, lightFrom, lightFrom + member, -sashHalf, sashHalf, sashBottom, sashTop,
                SurfaceKind.OpeningFrame);
            AddWallBlock(mesh, wall, lightTo - member, lightTo, -sashHalf, sashHalf, sashBottom, sashTop,
                SurfaceKind.OpeningFrame);
            AddWallBlock(mesh, wall, lightFrom + member, lightTo - member, -sashHalf, sashHalf,
                sashTop - member, sashTop, SurfaceKind.OpeningFrame);
            AddWallBlock(mesh, wall, lightFrom + member, lightTo - member, -sashHalf, sashHalf,
                sashBottom, sashBottom + member, SurfaceKind.OpeningFrame);
            AddWallBlock(mesh, wall, lightFrom + member, lightTo - member, -glassHalf, glassHalf,
                sashBottom + member, sashTop - member, SurfaceKind.Glass);
        }
    }

    /// <summary>Adds a projecting window sill that overhangs both wall faces and the opening by 3 cm.</summary>
    private static void AddWindowSill(SceneMesh mesh, Wall wall, WallOpening opening, double bottom)
    {
        const double overhang = 0.03;
        const double thickness = 0.03;
        var projection = wall.Thickness / 2 + 0.02;
        AddWallBlock(mesh, wall, opening.Begin - overhang, opening.End + overhang, -projection, projection,
            bottom - thickness, bottom, SurfaceKind.OpeningFrame);
    }

    /// <summary>
    /// Adds an oriented box in wall coordinates: <paramref name="from"/> and <paramref name="to"/> run
    /// along the wall axis and <paramref name="sideA"/> and <paramref name="sideB"/> measure across it,
    /// all in meters from the wall centerline. Negative and positive sides may be given in any order.
    /// </summary>
    private static void AddWallBlock(SceneMesh mesh, Wall wall, double from, double to, double sideA, double sideB,
        double bottom, double top, SurfaceKind kind)
    {
        var length = wall.Length;
        var direction = new Vector2((float)((wall.End.X - wall.Start.X) / length),
            (float)((wall.End.Y - wall.Start.Y) / length));
        var normal = new Vector2(-direction.Y, direction.X);
        var origin = new Vector2((float)wall.Start.X, (float)wall.Start.Y);
        var a = origin + direction * (float)from;
        var b = origin + direction * (float)to;
        var near = normal * (float)sideA;
        var far = normal * (float)sideB;
        AddPrism(mesh, [a + near, b + near, b + far, a + far], (float)bottom, (float)top, kind);
    }

    /// <summary>Adds a full-thickness wall section whose plan outline is already mitered at joined corners.</summary>
    private static void AddWallSection(SceneMesh mesh, IReadOnlyList<Point2> outline, double bottom, double top,
        SurfaceKind kind) =>
        AddPrism(mesh, outline.Select(point => new Vector2((float)point.X, (float)point.Y)).ToArray(),
            (float)bottom, (float)top, kind);

    /// <summary>
    /// The render surface of a flat slab: water for a pool, otherwise its finish. Exposed concrete reads as fair-faced
    /// concrete, a painted slab as painted plaster (a white soffit), asphalt as road surfacing, grass as lawn, anything else
    /// as flooring.
    /// </summary>
    /// <summary>
    /// Surface to edit when selecting a slab from the render viewport. Match the
    /// actual mesher: pitched roofs use roof tiles, topography uses ground,
    /// and flat slabs use their specified finish (including water and paint).
    /// A prism picker selects an element, not an individual triangle, so this
    /// intentionally targets the main visible surface rather than a side face.
    /// </summary>
    public static SurfaceKind SelectableSlabSurface(FloorSlab floor)
    {
        ArgumentNullException.ThrowIfNull(floor);
        return floor.IsToposolid ? SurfaceKind.Ground :
            floor.IsPitched ? SurfaceKind.Roof : SlabSurface(floor);
    }

    private static SurfaceKind SlabSurface(FloorSlab floor) => floor.Kind == FloorKind.Water
        ? SurfaceKind.Water
        : floor.Finish switch
        {
            FinishKind.ExposedConcrete => SurfaceKind.WallConcrete,
            FinishKind.Paint => SurfaceKind.Wall,
            FinishKind.Asphalt => SurfaceKind.Asphalt,
            FinishKind.Grass => SurfaceKind.Ground,
            _ => SurfaceKind.Floor
        };

    /// <summary>
    /// Adds a slab whose top face lies on the level elevation. Openings that fit inside the boundary are cut
    /// out: the top and bottom faces are built from convex pieces, and each opening gets its own inner walls.
    /// </summary>
    private static void AddSlab(SceneMesh mesh, IReadOnlyList<Point2> boundary, double elevation, double thickness,
        IReadOnlyList<Geometry.PlanRectangle> openings, SurfaceKind surface = SurfaceKind.Floor)
    {
        if (openings.Count == 0)
        {
            var outline = boundary.Select(point => new Vector2((float)point.X, (float)point.Y)).ToArray();
            AddPrism(mesh, outline, (float)(elevation - thickness), (float)elevation, surface);
            return;
        }

        var bottom = (float)(elevation - thickness);
        var top = (float)elevation;
        var ordered = PolygonTriangulator.SignedDoubleArea(boundary) > 0 ? boundary.ToArray() : boundary.Reverse().ToArray();
        foreach (var piece in SlabOpenings.Pieces(ordered, openings))
        {
            for (var corner = 1; corner < piece.Length - 1; corner++)
            {
                mesh.AddTriangle(LiftPoint(piece[0], top), LiftPoint(piece[corner], top),
                    LiftPoint(piece[corner + 1], top), SurfaceKind.Floor);
                mesh.AddTriangle(LiftPoint(piece[corner + 1], bottom), LiftPoint(piece[corner], bottom),
                    LiftPoint(piece[0], bottom), SurfaceKind.Floor);
            }
        }
        AddSideWalls(mesh, ordered, bottom, top);
        foreach (var opening in openings)
            // Walking the opening clockwise makes its walls face into the hole, away from the slab.
            AddSideWalls(mesh, Geometry.SlabOpenings.Corners(opening).Reverse().ToArray(), bottom, top);
    }

    /// <summary>Returns the openings that cut a floor: its own level's openings, and shafts from at or below it.</summary>
    private static IReadOnlyList<Geometry.PlanRectangle> FloorOpenings(BuildingDocument document, FloorSlab floor,
        IReadOnlyDictionary<Guid, double> elevations)
    {
        var floorElevation = elevations[floor.LevelId];
        return document.StructuralElements
            .Where(element => (element.Kind == StructuralElementKind.Opening && floor.Kind == FloorKind.Floor && element.LevelId == floor.LevelId) ||
                (element.Kind == StructuralElementKind.Shaft && elevations[element.LevelId] <= floorElevation) ||
                // The hoistway passes through every floor above the elevator's own level, up to its top landing.
                (element.Kind == StructuralElementKind.Elevator && elevations[element.LevelId] < floorElevation - 1e-6 &&
                    floorElevation <= elevations[element.LevelId] + ElevatorGeometry.Travel(element) + 1e-6) ||
                // A dormer opens the roof it stands on; other slabs keep their solid top.
                (element.Kind is StructuralElementKind.Dormer or StructuralElementKind.Skylight && floor.Kind == FloorKind.Roof &&
                    element.LevelId == floor.LevelId))
            .Select(element => Geometry.PlanRectangle.Of(element.Footprint))
            .Where(rectangle => Geometry.SlabOpenings.ContainsRectangle(floor.Boundary, rectangle))
            .ToArray();
    }

    /// <summary>Adds the vertical quads of a closed plan outline, each facing outward from the enclosed area.</summary>
    private static void AddSideWalls(SceneMesh mesh, IReadOnlyList<Point2> outline, float bottom, float top)
    {
        for (var index = 0; index < outline.Count; index++)
        {
            var current = outline[index];
            var next = outline[(index + 1) % outline.Count];
            mesh.AddQuad(LiftPoint(current, bottom), LiftPoint(next, bottom),
                LiftPoint(next, top), LiftPoint(current, top), SurfaceKind.Floor);
        }
    }

    /// <summary>Adds one element part as a prism, or as a sloped wedge when it carries an end height.</summary>
    private static void AddPart(SceneMesh mesh, ElementPart part, double datum)
    {
        var outline = part.Footprint.Select(point => new Vector2((float)point.X, (float)point.Y)).ToArray();
        if (part.EndTop is not double endTop || outline.Length != 4)
        {
            AddPrism(mesh, outline, (float)(datum + part.Bottom), (float)(datum + part.Top), part.Kind);
            return;
        }
        // Vertices 0 and 3 sit at the start (Top); vertices 1 and 2 at the end (EndTop).
        var bottom = (float)(datum + part.Bottom);
        var endBottom = (float)(datum + (part.EndBottom ?? part.Bottom));
        float[] bottoms = [bottom, endBottom, endBottom, bottom];
        float[] tops = [(float)(datum + part.Top), (float)(datum + endTop), (float)(datum + endTop), (float)(datum + part.Top)];
        var counterClockwise = PolygonTriangulator.SignedDoubleArea(
            outline.Select(point => new Point2(point.X, point.Y)).ToArray()) > 0;
        var order = counterClockwise ? new[] { 0, 1, 2, 3 } : new[] { 3, 2, 1, 0 };
        Vector3 Top(int index) => new(outline[order[index]].X, outline[order[index]].Y, tops[order[index]]);
        Vector3 Bottom(int index) => new(outline[order[index]].X, outline[order[index]].Y, bottoms[order[index]]);
        mesh.AddQuad(Top(0), Top(1), Top(2), Top(3), part.Kind);
        mesh.AddQuad(Bottom(3), Bottom(2), Bottom(1), Bottom(0), part.Kind);
        for (var index = 0; index < 4; index++)
        {
            var next = (index + 1) % 4;
            mesh.AddQuad(Bottom(index), Bottom(next), Top(next), Top(index), part.Kind);
        }
    }

    /// <summary>
    /// Adds a pitched roof as planar facets. The eave sits at <paramref name="eaveTop"/>; each facet rises linearly,
    /// its underside follows it a slab thickness lower, and the exterior edges close the section.
    /// </summary>
    private static void AddPitchedRoof(SceneMesh mesh, FloorSlab roof, double eaveTop)
    {
        var boundary = PolygonTriangulator.SignedDoubleArea(roof.Boundary) > 0 ? roof.Boundary : [.. roof.Boundary.AsEnumerable().Reverse()];
        Vector3 Surface(Point2 point, double drop) =>
            new((float)point.X, (float)point.Y, (float)(eaveTop + RoofGeometry.RiseAt(roof, point) - drop));
        foreach (var facet in RoofGeometry.Facets(roof))
        {
            IReadOnlyList<(int A, int B, int C)> triangles;
            try { triangles = PolygonTriangulator.Triangulate(facet.Outline); }
            catch (ArgumentException) { continue; }
            foreach (var (a, b, c) in triangles)
            {
                mesh.AddTriangle(Surface(facet.Outline[a], 0), Surface(facet.Outline[b], 0), Surface(facet.Outline[c], 0),
                    SurfaceKind.Roof);
                mesh.AddTriangle(Surface(facet.Outline[c], roof.Thickness), Surface(facet.Outline[b], roof.Thickness),
                    Surface(facet.Outline[a], roof.Thickness), SurfaceKind.Floor);
            }
            for (var index = 0; index < facet.Outline.Count; index++)
            {
                var current = facet.Outline[index];
                var next = facet.Outline[(index + 1) % facet.Outline.Count];
                if (!LiesOnBoundary(boundary, new Point2((current.X + next.X) / 2, (current.Y + next.Y) / 2))) continue;
                mesh.AddQuad(Surface(current, roof.Thickness), Surface(next, roof.Thickness), Surface(next, 0),
                    Surface(current, 0), SurfaceKind.Floor);
            }
        }
    }

    /// <summary>
    /// Closes the gables of a pitched roof: along every boundary edge where the roof rises, a wall 20 cm thick fills the
    /// space from the wall top (the eave underside) up to the roof's underside, as Revit's walls attached to a roof do.
    /// </summary>
    private static void AddGableWalls(SceneMesh mesh, FloorSlab roof, double eaveTop)
    {
        const double thickness = 0.20;
        var boundary = PolygonTriangulator.SignedDoubleArea(roof.Boundary) > 0 ? roof.Boundary : [.. roof.Boundary.AsEnumerable().Reverse()];
        var bottom = (float)(eaveTop - roof.Thickness);
        for (var index = 0; index < boundary.Count; index++)
        {
            var from = boundary[index];
            var to = boundary[(index + 1) % boundary.Count];
            if (RoofGeometry.GableProfile(roof, from, to) is not { } profile) continue;
            var length = from.DistanceTo(to);
            if (length < 1e-6) continue;
            // Counter-clockwise boundary: the inside is to the left of each edge.
            var inward = new Point2(-(to.Y - from.Y) / length * thickness, (to.X - from.X) / length * thickness);
            for (var sample = 0; sample + 1 < profile.Count; sample++)
            {
                var (a, riseA) = profile[sample];
                var (b, riseB) = profile[sample + 1];
                var topA = (float)(eaveTop + riseA - roof.Thickness);
                var topB = (float)(eaveTop + riseB - roof.Thickness);
                if (topA - bottom < 1e-4 && topB - bottom < 1e-4) continue;
                var innerA = new Point2(a.X + inward.X, a.Y + inward.Y);
                var innerB = new Point2(b.X + inward.X, b.Y + inward.Y);
                mesh.AddQuad(LiftPoint(a, bottom), LiftPoint(b, bottom), LiftPoint(b, topB), LiftPoint(a, topA), SurfaceKind.Wall);
                mesh.AddQuad(LiftPoint(innerB, bottom), LiftPoint(innerA, bottom), LiftPoint(innerA, topA), LiftPoint(innerB, topB), SurfaceKind.InteriorWall);
                mesh.AddQuad(LiftPoint(a, topA), LiftPoint(b, topB), LiftPoint(innerB, topB), LiftPoint(innerA, topA), SurfaceKind.Wall);
            }
        }
    }

    /// <summary>Tests whether a point lies on any edge of a closed polygon, within a micrometer.</summary>
    private static bool LiesOnBoundary(IReadOnlyList<Point2> polygon, Point2 point)
    {
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            var edge = a.DistanceTo(b);
            if (edge < 1e-12) continue;
            var cross = Math.Abs((b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X)) / edge;
            var along = ((point.X - a.X) * (b.X - a.X) + (point.Y - a.Y) * (b.Y - a.Y)) / edge;
            if (cross < 1e-6 && along >= -1e-6 && along <= edge + 1e-6) return true;
        }
        return false;
    }

    /// <summary>Raises a plan point to a model elevation.</summary>
    private static Vector3 LiftPoint(Point2 point, float z) => new((float)point.X, (float)point.Y, z);

    /// <summary>Adds a rectangular column standing on its level, up to the resolved column top.</summary>
    private static void AddColumn(SceneMesh mesh, Column column, double elevation, IReadOnlyList<Level> levels)
    {
        var outline = column.Outline.Select(point => new Vector2((float)point.X, (float)point.Y)).ToArray();
        AddPrism(mesh, outline, (float)elevation, (float)ColumnTop(column, elevation, levels), SurfaceKind.Wall);
    }

    /// <summary>Resolves the top elevation, in meters, of a column whose base sits at the given elevation.</summary>
    private static double ColumnTop(Column column, double baseElevation, IReadOnlyList<Level> levels)
    {
        // TODO(human): decide how the column top is resolved. The default below trusts the stored height.
        return baseElevation + column.Height;
    }

    /// <summary>Extrudes a simple polygon vertically into a closed, outward-facing prism.</summary>
    private static void AddPrism(SceneMesh mesh, IReadOnlyList<Vector2> outline, float bottom, float top,
        SurfaceKind kind, Func<Vector2, Vector2, SurfaceKind>? sideKind = null, Func<Vector2, float>? topAt = null)
    {
        float TopOf(Vector2 corner) => topAt?.Invoke(corner) ?? top;
        var plan = outline.Select(point => new Point2(point.X, point.Y)).ToArray();
        var counterClockwise = PolygonTriangulator.SignedDoubleArea(plan) > 0;
        var ordered = counterClockwise ? outline.ToArray() : outline.Reverse().ToArray();
        var orderedPlan = ordered.Select(point => new Point2(point.X, point.Y)).ToArray();
        // Far from the origin a float can no longer tell the corners of a thin outline apart, and its caps collapse to
        // no area. The sides still stand, and one flattened element must not stop the whole model from meshing.
        IReadOnlyList<(int A, int B, int C)> caps;
        try { caps = PolygonTriangulator.Triangulate(orderedPlan); }
        catch (ArgumentException) { caps = []; }
        foreach (var (ia, ib, ic) in caps)
        {
            mesh.AddTriangle(Lift(ordered[ia], TopOf(ordered[ia])), Lift(ordered[ib], TopOf(ordered[ib])), Lift(ordered[ic], TopOf(ordered[ic])), kind);
            mesh.AddTriangle(Lift(ordered[ic], bottom), Lift(ordered[ib], bottom), Lift(ordered[ia], bottom), kind);
        }
        for (var index = 0; index < ordered.Length; index++)
        {
            var current = ordered[index];
            var next = ordered[(index + 1) % ordered.Length];
            var side = kind;
            if (sideKind is not null && Vector2.DistanceSquared(current, next) > 1e-12f)
            {
                // Counter-clockwise outline: the outward normal of an edge is its direction turned clockwise.
                var direction = Vector2.Normalize(next - current);
                side = sideKind((current + next) / 2, new Vector2(direction.Y, -direction.X));
            }
            mesh.AddQuad(Lift(current, bottom), Lift(next, bottom), Lift(next, TopOf(next)), Lift(current, TopOf(current)), side);
        }
    }

    /// <summary>Raises a plan point to a model elevation for prism faces.</summary>
    private static Vector3 Lift(Vector2 point, float z) => new(point.X, point.Y, z);
}
