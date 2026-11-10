using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Interop;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks that a newer model reaches a render project without discarding the render work.</summary>
internal static class RenderModelUpdateChecks
{
    /// <summary>Builds a small house the way the modeling application would export it.</summary>
    private static (DocumentSession Session, Guid[] Walls, Guid Car) ExportedHouse()
    {
        var session = new DocumentSession(BuildingDocument.Create("Model update checks"));
        var level = session.Document.Levels[0].Id;
        Guid[] walls =
        [
            session.AddWall(level, new Point2(0, 0), new Point2(8, 0)),
            session.AddWall(level, new Point2(8, 0), new Point2(8, 6)),
            session.AddWall(level, new Point2(8, 6), new Point2(0, 6)),
            session.AddWall(level, new Point2(0, 6), new Point2(0, 0)),
            session.AddWall(level, new Point2(4, 0), new Point2(4, 6))
        ];
        var car = session.AddStructuralElement(level, StructuralElementKind.Component, "Car", new Point2(12, 2), new Point2(12, 2));
        session.StoreRenderSetup(new RenderSetup { CameraX = -6, CameraY = -8, CameraZ = 1.7, SolarHour = 9 });
        return (session, walls, car);
    }

    /// <summary>Exercises three-way updates, conflicts, the fallback without history, the sidecar and undo.</summary>
    internal static void Run(Action<bool, string> check)
    {
        var (model, walls, car) = ExportedHouse();
        var exported = model.Document.Clone();
        var level = exported.Levels[0].Id;

        // The render application dresses the scene and sets its own view.
        var render = new DocumentSession(exported.Clone());
        var tree = render.AddStructuralElement(level, StructuralElementKind.Component, "Tree", new Point2(-3, 3), new Point2(-3, 3));
        var photo = render.AddRenderScene("Street", new RenderSetup { CameraX = 14, CameraY = -9, SolarHour = 17 });
        render.SetRenderMaterial(new RenderMaterialAssignment(SurfaceKind.Wall, "builtin:brick") { ElementId = walls[4] });
        render.SetRenderMaterial(new RenderMaterialAssignment(SurfaceKind.Wall, "builtin:plaster") { ElementId = walls[0] });
        render.SetRenderMaterial(new RenderMaterialAssignment(SurfaceKind.Floor, "builtin:wood"));
        render.MoveStructuralElement(car, 1, 0);
        render.StoreRenderSetup(new RenderSetup { CameraX = 20, CameraY = 4, CameraZ = 1.6, SolarHour = 16.5 });

        // Meanwhile the architect removes a partition, moves a facade, adds a wall and nudges the same car.
        model.RemoveWall(walls[4]);
        model.MoveWall(walls[1], 1.5, 0);
        var added = model.AddWall(level, new Point2(0, 6), new Point2(0, 9));
        model.MoveStructuralElement(car, 0, 5);
        model.StoreRenderSetup(new RenderSetup { CameraX = 99, SolarHour = 6 });
        var incoming = model.Document.Clone();

        var result = RenderModelUpdate.Merge(exported, render.Document, incoming);
        var merged = result.Document;
        var report = result.Report;
        check(merged.Walls.Any(wall => wall.Id == added) && merged.Walls.All(wall => wall.Id != walls[4]) &&
              merged.Walls.Single(wall => wall.Id == walls[1]).Start.X == 9.5,
            "Model update brings added, moved and removed walls into the render project");
        check(merged.StructuralElements.Any(element => element.Id == tree) && merged.RenderScenes.Single().Id == photo &&
              merged.RenderSetup!.CameraX == 20 && merged.RenderSetup.SolarHour == 16.5,
            "Model update keeps render-placed objects, photos and the render view");
        check(merged.RenderMaterials.Count == 2 && merged.RenderMaterials.Any(material => material.ElementId == walls[0]) &&
              merged.RenderMaterials.All(material => material.ElementId != walls[4]) && report.MaterialsDropped == 1,
            "Model update keeps render materials and drops only those of elements that left the model");
        var mergedCar = merged.StructuralElements.Single(element => element.Id == car);
        check(mergedCar.Start.X == 13 && mergedCar.Start.Y == 2 && report.Conflicts.Count == 1 &&
              report.Conflicts[0].Resolution == "KeptMine",
            "A scene component moved in both applications keeps its render position and is reported as a conflict");
        // Moving a facade also moves the walls joined to it, so more than one wall counts as changed.
        var wallChanges = report.ByCollection[nameof(BuildingDocument.Walls)];
        check(report.UsedBaseline && wallChanges is { Added: 1, Removed: 1, Changed: >= 1 } && report.ProjectChanged,
            $"Model update reports the wall changes it applied ({wallChanges.Added} added, {wallChanges.Changed} changed, {wallChanges.Removed} removed)");

        render.ApplyModelUpdate(merged, "token-1");
        check(RenderModelUpdate.SyncTokenOf(render.Document) == "token-1" && render.Document.Walls.Count == 5,
            "Applying a model update stamps the project with its sync token");
        var again = RenderModelUpdate.Merge(incoming, render.Document, incoming);
        check(!again.Report.HasChanges && again.Document.StructuralElements.Any(element => element.Id == tree),
            "Repeating an update with an unchanged model changes nothing");
        render.Undo();
        check(RenderModelUpdate.SyncTokenOf(render.Document) == string.Empty && render.Document.Walls.Any(wall => wall.Id == walls[4]) &&
              render.Document.RenderMaterials.Count == 3,
            "Undoing a model update restores the previous model, materials and sync token");

        // A project that predates links has no common model: the building follows the model, scene dressing stays.
        var withoutHistory = RenderModelUpdate.Merge(null, render.Document, incoming);
        check(!withoutHistory.Report.UsedBaseline && withoutHistory.Document.Walls.All(wall => wall.Id != walls[4]) &&
              withoutHistory.Document.Walls.Any(wall => wall.Id == added) &&
              withoutHistory.Document.StructuralElements.Any(element => element.Id == tree) &&
              withoutHistory.Document.RenderScenes.Count == 1 && withoutHistory.Document.RenderSetup!.CameraX == 20,
            "Without a recorded common model the building follows the model and render work is still kept");

        var folder = Path.Combine(Path.GetTempPath(), $"g3d-model-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var modelPath = Path.Combine(folder, "house.g3d");
            var projectPath = Path.Combine(folder, "house-render.gr");
            ProjectFile.Save(modelPath, incoming);
            var link = new RenderModelLink { SourcePath = modelPath, SyncToken = "token-1", Baseline = incoming }.WithStampOf(modelPath);
            RenderModelLinkFile.Save(projectPath, link);
            var loaded = RenderModelLinkFile.Load(projectPath)!;
            check(loaded.SyncToken == "token-1" && loaded.Baseline!.Walls.Count == incoming.Walls.Count &&
                  loaded.RelativeSourcePath == "house.g3d" && !loaded.SourceChanged(modelPath),
                "The model link survives save and reopen with its baseline and source stamp");
            File.Move(modelPath, Path.Combine(folder, "moved.g3d"));
            check(RenderModelLinkFile.ResolveSource(loaded, projectPath) is null, "A missing model file is reported as unresolved");
            File.Move(Path.Combine(folder, "moved.g3d"), modelPath);
            model.AddWall(level, new Point2(8, 6), new Point2(8, 9));
            ProjectFile.Save(modelPath, model.Document);
            check(loaded.SourceChanged(RenderModelLinkFile.ResolveSource(loaded, projectPath)),
                "Saving the model again is detected as a pending update");
            File.WriteAllText(RenderModelLinkFile.SidecarPath(projectPath), "not a link");
            check(RenderModelLinkFile.Load(projectPath) is null, "A damaged model link is ignored instead of blocking the project");

            var relative = new BuildingDocument { RenderMaterials = [new RenderMaterialAssignment(SurfaceKind.Wall, "textures/brick.png") { NormalPath = "pbr:maps/n.png" }, new RenderMaterialAssignment(SurfaceKind.Floor, "builtin:wood")] };
            RenderModelUpdate.RebaseAssets(relative, folder);
            check(relative.RenderMaterials[0].TexturePath == Path.Combine(folder, "textures", "brick.png") &&
                  relative.RenderMaterials[0].NormalPath == "pbr:" + Path.Combine(folder, "maps", "n.png") &&
                  relative.RenderMaterials[1].TexturePath == "builtin:wood",
                "Relative model textures are rebased to the model folder and built-in materials are untouched");

            // The modeling application exports, the render application saves work into that file, and a second
            // export of a changed model must not discard that work.
            var exportPath = Path.Combine(folder, "house-export.g3d");
            var first = model.Document.Clone();
            check(RenderModelExport.Write(exportPath, first, modelPath) == RenderExportOutcome.Written &&
                  RenderModelLinkFile.Load(exportPath) is { Baseline: not null } exportLink &&
                  exportLink.SyncToken == RenderModelUpdate.SyncTokenOf(ProjectFile.Load(exportPath)) && exportLink.SourcePath == modelPath,
                "A render export carries a model link that matches the exported project");
            var studio = new DocumentSession(ProjectFile.Load(exportPath));
            var shrub = studio.AddStructuralElement(level, StructuralElementKind.Component, "Shrub", new Point2(-2, -2), new Point2(-2, -2));
            ProjectFile.Save(exportPath, studio.Document);
            var annex = model.AddWall(level, new Point2(-5, 0), new Point2(-5, 4));
            check(RenderModelExport.Write(exportPath, model.Document.Clone(), modelPath) == RenderExportOutcome.MergedWithRenderWork &&
                  ProjectFile.Load(exportPath) is var again2 && again2.StructuralElements.Any(element => element.Id == shrub) &&
                  again2.Walls.Any(wall => wall.Id == annex),
                "Exporting again over a file with render work merges the new model instead of replacing that work");
            File.Delete(RenderModelLinkFile.SidecarPath(exportPath));
            check(RenderModelExport.Write(exportPath, model.Document.Clone(), modelPath) == RenderExportOutcome.Written &&
                  ProjectFile.Load(exportPath).StructuralElements.All(element => element.Id != shrub) && File.Exists(exportPath + ".bak"),
                "Without a model link an export replaces the file and leaves the previous one as a backup");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
