using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{
    /// <summary>
    /// Draws two four-wall chains with the real placement handler, ending each a little off its first point, and checks
    /// that the last click is pulled onto that point, the loop is joined and the chain ends. The second loop is drawn
    /// from the exterior face, where the closing corner lies off the clicked point.
    /// </summary>
    private static void WallLoopClosingChecks(DocumentSession session)
    {
        SetField("_scale", 60.0);
        SetField("_panOffset", new Point(0, 0));
        Invoke("SetWallTool", true);
        ((CheckBox)_window.FindName("WallChainCheck")).IsChecked = true;
        ((CheckBox)_window.FindName("WallOrthogonalCheck")).IsChecked = false;
        var location = (ComboBox)_window.FindName("WallLocationLinePicker");
        foreach (var (reference, origin) in new[] { ("Center", new Point2(30, 30)), ("Exterior", new Point2(30, 40)) })
        {
            location.SelectedItem = location.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag as string == reference) ?? location.Items[0];
            var before = session.Document.Walls.Count;
            var corners = new[]
            {
                origin, new Point2(origin.X + 6, origin.Y), new Point2(origin.X + 6, origin.Y + 3),
                new Point2(origin.X, origin.Y + 3), new Point2(origin.X + 0.12, origin.Y + 0.25)
            };
            foreach (var corner in corners)
            {
                var pixel = (Point)Invoke("WorldToCanvas", corner)!;
                Invoke("HandleWallPlacementClick", pixel);
                Pump();
            }
            var loop = session.Document.Walls.Skip(before).ToArray();
            Check(loop.Length == 4 && loop[^1].End.DistanceTo(loop[0].Start) < 1e-6,
                $"A wall chain drawn from the {reference.ToLowerInvariant()} reference closes on its first wall from a nearby click");
            Check(Field<Point2?>("_pendingWallStart") is null && Field<object?>("_chainTail") is null,
                "Closing the loop ends the chain and leaves the wall tool armed for a new wall");
        }
        Invoke("SetWallTool", false);
    }

    /// <summary>
    /// Arms the site component command, places a tree, steps to the next species with Space and places it, then checks
    /// both plantings and their plan symbols.
    /// </summary>
    private static void PlantingChecks(DocumentSession session)
    {
        Invoke("ExecuteApplicationCommand", "SiteComponent");
        var before = session.Document.StructuralElements.Count(element => element.Kind == StructuralElementKind.Planting);
        Invoke("CreateStructuralFromTool", StructuralElementKind.Planting, new Point2(-20, 20), new Point2(-20, 20));
        Check(Invoke("TryCyclePlantingSpecies") is true, "Space steps to the next planting species while the tool is armed");
        Invoke("CreateStructuralFromTool", StructuralElementKind.Planting, new Point2(-10, 20), new Point2(-10, 20));
        var plants = session.Document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Planting).ToArray();
        Check(plants.Length == before + 2 && plants[^2].Style == ElementStyle.Default &&
              plants[^1].Style == ElementStyle.Guanacaste,
            "The site component command plants a broadleaf tree, then a guanacaste tree after Space");
        var (scale, pan) = (Field<double>("_scale"), Field<Point>("_panOffset"));
        SetField("_scale", 20.0);
        SetField("_panOffset", new Point(300, -400));
        Invoke("RenderPlan");
        Pump();
        Check(plants.TakeLast(2).All(plant => _canvas.Children.OfType<System.Windows.Shapes.Polygon>()
                .Any(shape => shape.Tag is Guid id && id == plant.Id)),
            "Each planting is drawn in plan with its canopy symbol and can be picked");
        Invoke("CancelStructuralTool");
        // The later render checks time their scene; leave it as it was.
        foreach (var plant in plants.TakeLast(2)) session.RemoveStructuralElement(plant.Id);
        SetField("_scale", scale);
        SetField("_panOffset", pan);
        Invoke("RefreshAll");
        Pump();
    }

    /// <summary>Arms the stair tool, switches to the L stair with Space, places it and checks its automatic railings.</summary>
    private static void StairToolChecks(DocumentSession session)
    {
        Invoke("BeginStairTool");
        Check(Invoke("TryCyclePlantingSpecies") is true && Invoke("TryCyclePlantingSpecies") is true,
            "Space steps through the stair types while the stair tool is armed");
        var before = session.Document.StructuralElements.Count;
        Invoke("CreateStructuralFromTool", StructuralElementKind.Stair, new Point2(-30, -20), new Point2(-26, -20));
        var added = session.Document.StructuralElements.Skip(before).ToArray();
        Check(added.Count(element => element.Kind == StructuralElementKind.Stair && element.Style == ElementStyle.LShaped) == 1 &&
              added.Count(element => element.Kind == StructuralElementKind.Railing) >= 4,
            "Placing an L stair adds it with railings on its flights and landing in one step");
        session.Undo();
        Check(session.Document.StructuralElements.Count == before, "One undo removes the stair together with its railings");
        Invoke("CancelStructuralTool");
    }

    /// <summary>Checks the shipped texture catalog and previews a photographic texture on model surfaces.</summary>
    private static void TexturedModelChecks(DocumentSession session)
    {
        CheckBuiltInTextures();
        // Modeling always uses project colors, including assigned textures.
        var tabs = (TabControl)_window.FindName("ViewTabs");
        var previousTab = tabs.SelectedIndex;
        tabs.SelectedIndex = 1;
        Invoke("AssignRenderMaterial", SurfaceKind.Wall, "builtin:ambientcg-bricks105", 2.4, 0.3);
        Invoke("RebuildModelVisual");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (Field<bool>("_modelDirty") && DateTime.UtcNow < deadline) Pump();
        var viewport = (System.Windows.Controls.Viewport3D)_window.FindName("ModelViewport");
        var textured = viewport.Children.OfType<System.Windows.Media.Media3D.ModelVisual3D>()
            .Select(visual => visual.Content).OfType<System.Windows.Media.Media3D.GeometryModel3D>()
            .Any(model => model.Material is System.Windows.Media.Media3D.DiffuseMaterial { Brush: ImageBrush } &&
                          model.Geometry is System.Windows.Media.Media3D.MeshGeometry3D { TextureCoordinates.Count: > 0 });
        Check(textured, "The 3D view previews an assigned render texture on its surfaces");
        Invoke("AssignRenderMaterial", SurfaceKind.Wall, null!, 1.0, 0.0);
        Check(session.Document.RenderMaterials.Count == 0, "Choosing the stock material clears the assignment");
        Invoke("RebuildModelVisual");
        tabs.SelectedIndex = previousTab;
    }

    /// <summary>
    /// Renders an isolated plan with an L of mixed thickness, a tee, a crossing, a three-wall corner,
    /// a single door and a pair of doors, so join cleanup and door symbols can be reviewed together.
    /// </summary>
    private static void RenderJoinScene()
    {
        var scene = new DocumentSession(BuildingDocument.Create());
        var level = scene.Document.Levels[0].Id;
        var host = scene.AddWall(level, new(0, 0), new(6, 0));
        scene.AddWall(level, new(6, 0), new(6, 4), 0.3);
        scene.AddWall(level, new(0, 0), new(0, 4));
        var top = scene.AddWall(level, new(0, 4), new(6, 4));
        scene.AddWall(level, new(0, 4), new(-3, 4));
        scene.AddWall(level, new(3, 0), new(3, 4), 0.15);
        scene.AddWall(level, new(1.5, 2), new(4.5, 2), 0.15);
        scene.AddOpening(host, OpeningKind.Door, 1.5);
        scene.AddOpening(top, OpeningKind.Door, 4.5, openingTypeId: BuiltInOpeningTypes.DoubleDoor1500Id);
        SetField("_session", scene);
        SetField("_activeLevelId", level);
        Invoke("SetWallTool", false);
        Invoke("FitPlan_Click", _window, new RoutedEventArgs());
        Pump();
        SaveBitmap("artifacts/interaction-evidence/wall-joins-and-doors.png");
        // A close-up at 1:20-like zoom, centred on the single door, shows linings, casings and the leaf.
        SetField("_scale", 420.0);
        SetField("_panOffset", new Point(-1.5 * 420, 0.35 * 420));
        Invoke("RenderPlan");
        Pump();
        SaveBitmap("artifacts/interaction-evidence/door-detail.png");
        Check(_canvas.Children.OfType<System.Windows.Shapes.Polygon>().Count(polygon => polygon.Tag is Guid) > 0,
            "Join and door review scene renders");
    }
}
