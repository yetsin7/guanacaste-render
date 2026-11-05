using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Import;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// Checks walk-through navigation (look, fly, pan, zoom toward a point, collisions), camera paths, linked-model files,
/// the light proxy for heavy models, the live preview and the render safety limits.
/// </summary>
static class NavigationChecks
{
    /// <summary>Validates navigation, rendering lifecycle, scene links, and bounded camera tours.</summary>
    public static void Run(Action<bool, string> check)
    {
        Camera(check);
        OrbitPivotChecks.Run(check);
        Collisions(check);
        Paths(check);
        Links(check);
        Proxy(check);
        Edges(check);
        Picking(check);
        WallChains(check);
        Elevations(check);
        Safety(check);
        LivePreviewChecks.Run(check).GetAwaiter().GetResult();
        Tours(check).GetAwaiter().GetResult();
    }

    private static bool Near(Vector3 a, Vector3 b, float tolerance = 1e-3f) => Vector3.Distance(a, b) < tolerance;

    private static void Camera(Action<bool, string> check)
    {
        var camera = new NavigationCamera { Position = Vector3.Zero, YawDegrees = 0, PitchDegrees = 0 };
        check(Near(camera.Forward, Vector3.UnitX) && Near(camera.Right, -Vector3.UnitY) && Near(camera.Up, Vector3.UnitZ),
            "A camera at yaw 0 looks along +X with +Y on its left");
        camera.Look(40, 0, 0.5f);
        check(Math.Abs(camera.YawDegrees - 340) < 1e-3 && camera.Forward.Y < 0, "Dragging right turns the view clockwise (yaw decreases)");
        camera.Look(0, -1000, 0.5f);
        check(camera.PitchDegrees == 89, "Looking up stops at 89° so the horizon can never flip");
        camera.PitchDegrees = 0;
        var move = camera.FlyDelta(new Vector3(1, 0, 0), 2);
        check(Near(move, camera.Forward * 2), "W flies exactly where the camera looks");
        var diagonal = camera.FlyDelta(new Vector3(1, 1, 0), 2);
        check(MathF.Abs(diagonal.Length() - 2) < 1e-4f, "Flying diagonally is not faster than flying straight");
        check(camera.FlyDelta(Vector3.Zero, 5) == Vector3.Zero, "No key pressed means no movement");

        var looking = new NavigationCamera { Position = new Vector3(0, 0, 0), YawDegrees = 0, PitchDegrees = 0 };
        var centre = looking.PixelRay(50, 50, 100, 100);
        var corner = looking.PixelRay(0, 0, 100, 100);
        check(Near(centre, looking.Forward) && corner.Y > 0 && corner.Z > 0, "The center pixel looks straight ahead and the top-left looks up and left");
        var before = looking.Position;
        looking.DollyToward(corner, 3);
        check(Math.Abs(Vector3.Distance(before, looking.Position) - 3) < 1e-4f && Near(looking.Forward, Vector3.UnitX),
            "Zooming toward the cursor moves along the pixel's ray without turning the camera");

        var orbit = new NavigationCamera { Position = new Vector3(10, 0, 0), YawDegrees = 180, PitchDegrees = 0 };
        orbit.OrbitAround(Vector3.Zero, 50, 20);
        check(MathF.Abs(orbit.Position.Length() - 10) < 1e-3f && Near(Vector3.Normalize(-orbit.Position), orbit.Forward, 1e-3f),
            "Orbiting keeps the distance to the pivot and keeps the pivot centered");

        var render = new NavigationCamera { Position = new Vector3(3, 4, 5), YawDegrees = 40, PitchDegrees = -20, FieldOfViewDegrees = 60 }.ToRenderCamera(8);
        var back = NavigationCamera.FromRenderCamera(render);
        check(Near(back.Position, new Vector3(3, 4, 5)) && Math.Abs(back.YawDegrees - 40) < 0.05f && Math.Abs(back.PitchDegrees + 20) < 0.05f && back.FieldOfViewDegrees == 60,
            "The navigation camera converts to a render camera and back without drift");
        var framed = NavigationCamera.Frame(new Vector3(0, 0, 0), new Vector3(12, 8, 3));
        check((framed.Position - new Vector3(6, 4, 1.5f)).Length() > 5 && Near(Vector3.Normalize(new Vector3(6, 4, 1.5f) - framed.Position), framed.Forward, 0.02f),
            "Framing a model puts the camera away from it and aims at its center");
        var framing = new NavigationCamera { YawDegrees = 37, PitchDegrees = -24, FieldOfViewDegrees = 45 };
        var minimum = new Vector3(-16, -4, -1);
        var maximum = new Vector3(16, 4, 20);
        framing.FrameBounds(minimum, maximum, 0.7f);
        var fits = true;
        foreach (var x in new[] { minimum.X, maximum.X })
        foreach (var y in new[] { minimum.Y, maximum.Y })
        foreach (var z in new[] { minimum.Z, maximum.Z })
        {
            var projected = framing.Project(new Vector3(x, y, z), 700, 1000);
            fits &= projected is { X: >= 0 and <= 700, Y: >= 0 and <= 1000 };
        }
        check(fits && framing.YawDegrees == 37 && framing.PitchDegrees == -24,
            "Centering fits every box corner in a narrow viewport without changing orientation");
        var pan = new NavigationCamera { Position = Vector3.Zero, YawDegrees = 0, PitchDegrees = 0 };
        pan.Pan(10, 0, 0.1f);
        check(Near(pan.Position, new Vector3(0, 1, 0)), "Dragging the middle button to the right slides the scene right (camera moves left)");
    }

    private static void Collisions(Action<bool, string> check)
    {
        // A wall across x = 5 and a floor: the camera must stop 0.3 m short and slide along the wall.
        var mesh = new SceneMesh();
        mesh.AddQuad(new(5, -20, -2), new(5, 20, -2), new(5, 20, 5), new(5, -20, 5), SurfaceKind.Wall);
        mesh.AddQuad(new(-20, -20, 0), new(20, -20, 0), new(20, 20, 0), new(-20, 20, 0), SurfaceKind.Floor);
        var scene = new RenderSceneBuilder().AddBuilding(mesh).Build();
        var query = SceneQuery.Build(scene);
        var collision = query.Collision(0.3f);

        var stopped = collision.Move(new Vector3(0, 0, 1.5f), new Vector3(8, 0, 0));
        check(MathF.Abs(stopped.X - 4.7f) < 0.01f, "The camera stops one radius before a wall");
        var slid = collision.Move(new Vector3(0, 0, 1.5f), new Vector3(8, 4, 0));
        check(slid.X < 4.75f && slid.Y > 1, "Moving diagonally into a wall slides along it instead of stopping dead");
        var free = collision.Move(new Vector3(0, 0, 1.5f), new Vector3(2, 0, 0));
        check(Near(free, new Vector3(2, 0, 1.5f)), "Open space is crossed in full");
        var down = collision.Move(new Vector3(0, 0, 1.5f), new Vector3(0, 0, -5));
        check(down.Z >= 0.29f, "The camera cannot sink through the floor");
        check(query.Pick(new Vector3(0, 0, 1.5f), Vector3.UnitX) is { } wall && Math.Abs(wall - 5) < 1e-3f, "Picking measures the distance to the surface under a ray");
        check(query.Pick(new Vector3(0, 0, 1.5f), Vector3.UnitZ) is null, "Picking returns nothing when the ray hits no surface");
        check(collision.IsBlocked(new Vector3(4.9f, 0, 1.5f)) && !collision.IsBlocked(new Vector3(0, 0, 1.5f)), "A position inside the clearance of a wall is reported blocked");
    }

    private static void Paths(Action<bool, string> check)
    {
        var path = new CameraPath { Ease = false };
        path.Add(new CameraMark(new Vector3(0, 0, 0), new Vector3(10, 0, 0), 40, 0));
        path.Add(new CameraMark(new Vector3(10, 0, 0), new Vector3(10, 10, 0), 50, 4));
        path.Add(new CameraMark(new Vector3(10, 10, 0), new Vector3(0, 10, 0), 60, 6));
        check(Math.Abs(path.Duration - 10) < 1e-4f && path.FrameCount(30) == 301, "A path lasts the sum of its marks' durations and renders one frame per tick plus the last");
        check(Near(path.Sample(0).Position, Vector3.Zero) && Near(path.Sample(4).Position, new Vector3(10, 0, 0)) && Near(path.Sample(10).Position, new Vector3(10, 10, 0)),
            "The camera passes exactly through every mark");
        check(Math.Abs(path.Sample(2).FieldOfView - 45) < 1e-3f, "Field of view blends between marks");
        check(Near(path.Sample(-5).Position, Vector3.Zero) && Near(path.Sample(99).Position, new Vector3(10, 10, 0)), "Sampling outside the path clamps to its ends");
        var eased = new CameraPath { Ease = true };
        eased.Add(new CameraMark(Vector3.Zero, Vector3.UnitX, 45, 0));
        eased.Add(new CameraMark(new Vector3(10, 0, 0), Vector3.UnitX, 45, 10));
        check(eased.Sample(1).Position.X < 1 && eased.Sample(5).Position.X is > 4.9f and < 5.1f, "Easing starts gently and is balanced in the middle");
        var rejected = false;
        try { path.Add(new CameraMark(new Vector3(float.NaN, 0, 0), Vector3.Zero, 45, 1)); }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "A mark with a NaN position is rejected");
        path.Move(2, 0);
        check(Near(path.Marks[0].Position, new Vector3(10, 10, 0)), "Marks can be reordered");
        var single = new CameraPath();
        single.Add(new CameraMark(Vector3.One, Vector3.Zero, 45, 3));
        check(single.FrameCount(24) == 1 && Near(single.Frame(0, 24).Position, Vector3.One), "A single mark yields a single still frame");
    }

    private static void Links(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var model = Path.Combine(folder, "models", "tree.obj");
            Directory.CreateDirectory(Path.GetDirectoryName(model)!);
            File.WriteAllText(model, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            var project = Path.Combine(folder, "casa.g3d");
            ModelLinkFile.Save(project, [new ModelLink
            {
                Path = model, Name = "Árbol", Visible = false, UpAxis = UpAxis.Z, MetersPerUnit = 1, PlaceOnGround = true,
                Placement = new ModelPlacement(3, 4, 0.5, 90, 2)
            }]);
            var loaded = ModelLinkFile.Load(project);
            check(loaded.Count == 1 && loaded[0].Name == "Árbol" && !loaded[0].Visible && loaded[0].Placement == new ModelPlacement(3, 4, 0.5, 90, 2) &&
                  loaded[0].UpAxis == UpAxis.Z && loaded[0].PlaceOnGround, "Model links round-trip through their file, with accents, placement and options");
            check(File.Exists(ModelLinkFile.SidecarPath(project)) && !File.Exists(ModelLinkFile.SidecarPath(project) + ".tmp"), "Saving links leaves no temporary file behind");

            // The whole folder moves: the absolute path breaks but the relative one still finds the model.
            var moved = Path.Combine(folder, "moved");
            Directory.CreateDirectory(moved);
            Directory.Move(Path.Combine(folder, "models"), Path.Combine(moved, "models"));
            File.Move(ModelLinkFile.SidecarPath(project), ModelLinkFile.SidecarPath(Path.Combine(moved, "casa.g3d")));
            var relocated = ModelLinkFile.Load(Path.Combine(moved, "casa.g3d"));
            check(relocated.Count == 1 && File.Exists(relocated[0].Path), "A project moved with its models still finds them through relative paths");

            File.WriteAllText(ModelLinkFile.SidecarPath(project), "{ not json");
            var corrupt = false;
            try { ModelLinkFile.Load(project); }
            catch (InvalidDataException) { corrupt = true; }
            check(corrupt, "A damaged links file is reported instead of crashing");
            check(ModelLinkFile.Load(Path.Combine(folder, "none.g3d")).Count == 0, "A project without a links file has no links");

            File.WriteAllText(ModelLinkFile.SidecarPath(project), "[{\"Path\":\"x.obj\",\"Placement\":{\"Scale\":0}}]");
            check(ModelLinkFile.Load(project) is [{ } only] && only.Placement == ModelPlacement.Identity, "An invalid placement in a links file falls back to the identity");
            ModelLinkFile.Save(project, []);
            check(!File.Exists(ModelLinkFile.SidecarPath(project)), "Saving no links removes the links file");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Proxy(Action<bool, string> check)
    {
        // A finely tessellated 40 m × 40 m wavy surface of 80 000 triangles must shrink to a light proxy with the same footprint.
        const int n = 200;
        var positions = new List<Vector3>();
        for (var y = 0; y <= n; y++)
            for (var x = 0; x <= n; x++)
                positions.Add(new Vector3(x * 0.2f, y * 0.2f, MathF.Sin(x * 0.1f) * 0.5f));
        var indices = new List<int>();
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var i = y * (n + 1) + x;
                indices.AddRange([i, i + 1, i + n + 2, i, i + n + 2, i + n + 1]);
            }
        var model = new ImportedModel { Positions = [.. positions], Indices = [.. indices], TriangleMaterial = new int[indices.Count / 3], Materials = [MaterialLibrary.Imported] };
        var small = MeshProxy.Build(model, 2000);
        var (low, high) = model.Bounds();
        var proxyMin = small.Positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var proxyMax = small.Positions.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        check(small.IsReduced && small.TriangleCount is > 500 and < 3500 && small.SourceTriangles == 80_000, "A heavy mesh is reduced near the requested triangle budget");
        check(Vector3.Distance(proxyMin, low) < 1.5f && Vector3.Distance(proxyMax, high) < 1.5f, "The proxy keeps the footprint of the original");
        check(small.Indices.All(i => (uint)i < (uint)small.Positions.Length) && small.TriangleMaterial.Length == small.TriangleCount, "Every proxy triangle references a valid vertex and has a material");
        check(!MeshProxy.Build(model, 200_000).IsReduced, "A light model is shown as it is");
    }

    /// <summary>The outline drawn over the 3D view: a box has 12 edges; a flat quad split in two has 4, not 5.</summary>
    private static void Edges(Action<bool, string> check)
    {
        // Each corner is repeated per face, as real meshes do, so the weld has to join them.
        var positions = new List<Vector3>();
        var indices = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = positions.Count;
            positions.AddRange([a, b, c, d]);
            indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }
        Vector3 P(int x, int y, int z) => new(x, y, z);
        Quad(P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0));
        Quad(P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1));
        Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1));
        Quad(P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0));
        Quad(P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0));
        Quad(P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1));
        check(FeatureEdges.Extract(positions, indices).Count == 12, "feature edges: a closed box has exactly its 12 edges");

        var flat = FeatureEdges.Extract([P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0)], [0, 1, 2, 0, 2, 3]);
        check(flat.Count == 4, "feature edges: the diagonal inside a flat quad is not an edge");
        check(FeatureEdges.Extract([], []).Count == 0 && FeatureEdges.Extract(positions, indices, maximumEdges: 5).Count == 5,
            "feature edges: empty input is empty and the edge count is capped");
    }

    /// <summary>Clicking in the 3D view: the right element is found, openings win over their wall, and measures are exact.</summary>
    private static void Picking(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Pick"));
        var level = session.Document.Levels[0].Id;
        var wall = session.AddWall(level, new Point2(0, 0), new Point2(6, 0), 0.2, 3);
        var door = session.AddOpening(wall, OpeningKind.Door, 3);
        var column = session.AddColumn(level, new Point2(8, 0), 0.4, 0.4, 3);
        var picker = new ElementPicker(session.Document);

        var wallHit = picker.Pick(new Vector3(1, -5, 2.5f), Vector3.UnitY);
        check(wallHit is { Kind: PickedKind.Wall } && wallHit.Value.Id == wall && Math.Abs(wallHit.Value.Distance - 4.9f) < 1e-3f,
            "picking: a ray at a wall hits it on its near face");
        var doorHit = picker.Pick(new Vector3(3, -5, 1), Vector3.UnitY);
        check(doorHit is { Kind: PickedKind.Opening } && doorHit.Value.Id == door && doorHit.Value.HostId == wall,
            "picking: a ray at a door selects the door, not the wall that hosts it");
        check(picker.Pick(new Vector3(1, -5, 5), Vector3.UnitY) is null && picker.Pick(new Vector3(20, -5, 1), Vector3.UnitY) is null,
            "picking: a ray above the wall or beside everything hits nothing");
        check(picker.Pick(new Vector3(8, 0, 10), -Vector3.UnitZ) is { Kind: PickedKind.Column } top && top.Id == column &&
              Math.Abs(top.Distance - 7) < 1e-3f, "picking: a ray from above lands on a column's top");

        var measures = picker.Dimensions(PickedKind.Wall, wall).Select(line => line.Meters).OrderBy(value => value).ToArray();
        check(measures.Length == 3 && Math.Abs(measures[0] - 0.2f) < 1e-3f && Math.Abs(measures[2] - 6) < 1e-3f,
            "picking: a wall is measured by thickness, height and length");
        check(picker.Dimensions(PickedKind.Opening, door).Count >= 3 && picker.Outline(PickedKind.Column, column).Count == 12,
            "picking: openings are measured and a column outlines as 12 edges");

        // A concave U-shaped floor has a 3 m² notch. A triangle fan from the first vertex
        // overpaints that notch; the blue selection cap must have exactly the floor's 22 m².
        var concave = session.AddFloor(level,
            [new(0, 10), new(5, 10), new(5, 15), new(3, 15),
             new(3, 12), new(2, 12), new(2, 15), new(0, 15)], 0.2);
        var slab = session.Document.Floors.Single(item => item.Id == concave);
        var capTop = (float)(session.Document.Levels.Single(item => item.Id == level).Elevation + slab.ElevationOffset);
        var capBottom = capTop - (float)slab.Thickness;
        var tinted = new ElementPicker(session.Document).Fill(PickedKind.Floor, concave);
        static double CapArea(List<Vector3> triangles, float height) =>
            triangles.Chunk(3)
                .Where(triangle => triangle.Length == 3 && triangle.All(vertex => Math.Abs(vertex.Z - height) < 1e-5f))
                .Sum(triangle => Math.Abs(
                    (triangle[1].X - triangle[0].X) * (triangle[2].Y - triangle[0].Y) -
                    (triangle[1].Y - triangle[0].Y) * (triangle[2].X - triangle[0].X)) / 2d);
        check(Math.Abs(CapArea(tinted, capTop) - 22) < 1e-4 &&
              Math.Abs(CapArea(tinted, capBottom) - 22) < 1e-4,
            "picking: concave floor selection tints only the actual top and bottom footprint");

        var camera = new NavigationCamera { Position = new Vector3(0, -8, 2), YawDegrees = 90, PitchDegrees = 0, FieldOfViewDegrees = 50 };
        var ray = camera.PixelRay(300, 200, 800, 600);
        var onRay = camera.Position + ray * 7;
        var pixel = camera.Project(onRay, 800, 600);
        check(pixel is { } p && Math.Abs(p.X - 300) < 0.05f && Math.Abs(p.Y - 200) < 0.05f && camera.Project(new Vector3(0, -20, 2), 800, 600) is null,
            "picking: projecting a point on a pixel's ray returns that pixel, and points behind the camera have none");
    }

    /// <summary>The four elevations exist in every project, keep names the user chose, and can be renamed with undo.</summary>
    private static void Elevations(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Views"));
        var elevations = session.Document.Views.Where(view => view.Kind == ProjectViewKind.Elevation).ToList();
        check(elevations.Count == 4 && Enum.GetValues<ViewOrientation>().Where(o => o != ViewOrientation.None).All(o => elevations.Any(v => v.Orientation == o)) &&
              elevations.Select(view => view.Name).Distinct().Count() == 4 && elevations.Any(view => view.Name == "Vista Norte"),
            "views: a new project has four elevations named Vista Norte, Sur, Este and Oeste");

        var old = new List<ProjectView>
        {
            new(BuiltInProjectViews.NorthElevationId, "North Elevation", ProjectViewKind.Elevation, null, ViewOrientation.North),
            new(BuiltInProjectViews.SouthElevationId, "Fachada principal", ProjectViewKind.Elevation, null, ViewOrientation.South)
        };
        BuiltInProjectViews.EnsureElevations(old);
        check(old.Count == 4 && old.Single(view => view.Id == BuiltInProjectViews.NorthElevationId).Name == "Vista Norte" &&
              old.Single(view => view.Id == BuiltInProjectViews.SouthElevationId).Name == "Fachada principal",
            "views: an older project gains the missing elevations and keeps names the user chose");

        session.RenameView(BuiltInProjectViews.NorthElevationId, "  Fachada norte  ");
        check(session.Document.Views.Single(view => view.Id == BuiltInProjectViews.NorthElevationId).Name == "Fachada norte",
            "views: renaming an elevation trims the name");
        session.Undo();
        check(session.Document.Views.Single(view => view.Id == BuiltInProjectViews.NorthElevationId).Name == "Vista Norte",
            "views: undo restores the previous name");
        var duplicate = false;
        try { session.RenameView(BuiltInProjectViews.NorthElevationId, "vista sur"); }
        catch (InvalidDataException) { duplicate = true; }
        var empty = false;
        try { session.RenameView(BuiltInProjectViews.NorthElevationId, "   "); }
        catch (InvalidDataException) { empty = true; }
        check(duplicate && empty, "views: a name already in use, or an empty one, is refused");

        var added = session.AddElevationView("Garden elevation", ViewOrientation.North);
        check(session.Document.Views.Count(view => view.Kind == ProjectViewKind.Elevation) == 5 &&
              session.Document.Views.Single(view => view.Id == added).Orientation == ViewOrientation.North,
            "views: users can add another named elevation in an existing direction");
        var ground = session.Document.Levels.Single(level => level.Role == LevelRole.BaseFloor);
        session.AddWall(ground.Id, new Point2(-2, 0), new Point2(4, 0));
        var addedView = session.Document.Views.Single(view => view.Id == added);
        var marker = ElevationViewGeometry.AtPosition(session.Document, addedView.Orientation, new Point2(1, -4));
        session.UpdateElevationMarker(added, marker);
        check(ElevationViewGeometry.Resolve(session.Document, session.Document.Views.Single(view => view.Id == added)) == marker &&
              ElevationViewGeometry.Coverage(marker, ViewOrientation.North).Count == 4,
            "views: a manually positioned elevation remains fixed while its field covers the building");
        session.AddWall(ground.Id, new Point2(20, 0), new Point2(24, 0));
        check(ElevationViewGeometry.Resolve(session.Document, session.Document.Views.Single(view => view.Id == added)) != marker,
            "views: an elevation automatically refits only after the building grows outside its field");

        var plan = session.Document.Views.Single(view => view.Kind == ProjectViewKind.Plan && view.LevelId == ground.Id);
        var range = new PlanViewRange(0.8, 1, 2.6);
        session.UpdatePlanViewRange(plan.Id, range);
        check(session.Document.Views.Single(view => view.Id == plan.Id).Range == range &&
              range.Intersects(0.9, 2) && !range.Intersects(3, 4),
            "views: a plan persists a simple ordered range and tests vertical visibility");
        session.RemoveElevationView(added);
        check(session.Document.Views.All(view => view.Id != added), "views: an added elevation can be deleted");
        session.RemoveElevationView(BuiltInProjectViews.NorthElevationId);
        check(session.Document.Views.All(view => view.Id != BuiltInProjectViews.NorthElevationId),
            "views: a default cardinal elevation can be deleted while another elevation remains");
    }

    /// <summary>A chain of walls closes by itself when it ends on, or crosses, a wall that is already there.</summary>
    private static void WallChains(Action<bool, string> check)
    {
        var existing = new[] { new WallCenterline(new Point2(0, 0), new Point2(6, 0)) };
        bool Ends(Point2 from, Point2 to) => WallPlacementGeometry.EndsOnWall(new WallCenterline(from, to), existing, 0.1);
        check(Ends(new Point2(3, 4), new Point2(3, 0)), "wall chain: ending on another wall's centerline closes the chain");
        check(Ends(new Point2(3, 4), new Point2(3, -2)), "wall chain: crossing another wall closes the chain");
        check(!Ends(new Point2(3, 4), new Point2(3, 1)), "wall chain: stopping short of a wall keeps the chain going");
        check(!Ends(new Point2(3, 0), new Point2(3, 4)), "wall chain: starting on a wall and moving away does not close it");
        check(!Ends(new Point2(8, 0), new Point2(8, 4)), "wall chain: a wall beyond the end of another does not close it");
    }

    private static void Safety(Action<bool, string> check)
    {
        var plenty = new MemoryStatus(32L << 30, 24L << 30);
        var big = RenderSafety.FitToMemory(3840, 2160, 2_000_000, 400_000_000, plenty);
        check(big.Fits && !big.Reduced && big.Width == 3840, "A 4K render fits comfortably in plenty of memory");
        var tight = new MemoryStatus(8L << 30, 700L << 20);
        var reduced = RenderSafety.FitToMemory(3840, 2160, 500_000, 100_000_000, tight);
        check(reduced.Fits && reduced.Reduced && reduced.Width < 3840 && Math.Abs(reduced.Width / (double)reduced.Height - 16 / 9.0) < 0.03,
            "With little free memory the image is reduced, keeping its proportions, until it fits");
        check(reduced.EstimatedBytes <= tight.AvailableBytes * RenderSafety.PlanningShare, "A reduced size stays within the planned share of the free memory");
        var hopeless = RenderSafety.FitToMemory(1920, 1080, 50_000_000, 6_000_000_000, new MemoryStatus(8L << 30, 600L << 20));
        check(!hopeless.Fits, "A scene far larger than free memory is reported as not fitting rather than attempted");
        check(RenderSafety.ReadMemory() is { TotalBytes: > 0, AvailableBytes: > 0 } status && status.Load is >= 0 and <= 1, "Physical memory can be read from the operating system");
        var fired = new ManualResetEventSlim();
        using (new RenderSafety.Watchdog(1, fired.Set, TimeSpan.FromMilliseconds(30)))
            check(fired.Wait(TimeSpan.FromSeconds(5)), "The memory watchdog fires when the process exceeds its limit");
    }

    private static async Task Tours(Action<bool, string> check)
    {
        var mesh = new SceneMesh();
        mesh.AddQuad(new(-10, -10, 0), new(10, -10, 0), new(10, 10, 0), new(-10, 10, 0), SurfaceKind.Ground);
        var scene = new RenderSceneBuilder().AddBuilding(mesh).Build();
        var path = new CameraPath();
        path.Add(new CameraMark(new Vector3(-5, -5, 2), Vector3.Zero, 45, 0));
        path.Add(new CameraMark(new Vector3(5, -5, 2), Vector3.Zero, 45, 1));
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-tour-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reported = 0;
            var result = await TourRenderer.RenderAsync(scene, path, new RenderSettings(), 64, 36, 4, 3, new ImageFinish(), folder,
                new Progress<TourProgress>(_ => Interlocked.Increment(ref reported)), CancellationToken.None);
            var files = Directory.GetFiles(folder, "frame_*.png");
            check(result.FramesWritten == 5 && result.TotalFrames == 5 && files.Length == 5 && !result.Cancelled, "A one-second tour at 4 fps writes five numbered frames");
            check(files.Order().First().EndsWith("frame_00001.png", StringComparison.Ordinal) && PngCodec.Load(files[0]).Width == 64, "Tour frames are valid PNG images with the requested size");
            check(!PngCodec.Load(files[0]).Rgb.SequenceEqual(PngCodec.Load(files[4]).Rgb), "The first and last frames differ because the camera moved");

            using var cancellation = new CancellationTokenSource();
            var second = Path.Combine(folder, "second");
            var cancelled = await TourRenderer.RenderAsync(scene, path, new RenderSettings(), 64, 36, 24, 2, new ImageFinish(), second,
                new Progress<TourProgress>(p => { if (p.Frame >= 2) cancellation.Cancel(); }), cancellation.Token);
            check(cancelled.Cancelled && cancelled.FramesWritten >= 2 && cancelled.FramesWritten < cancelled.TotalFrames, "Cancelling a tour keeps the frames already written");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
