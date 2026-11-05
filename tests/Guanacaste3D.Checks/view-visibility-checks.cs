using Guanacaste3D.Core;

/// <summary>Checks per-view visibility and saved visibility templates across project persistence.</summary>
static class ViewVisibilityChecks
{
    /// <summary>Exercises independent views, batch application, undo, and a native project round trip.</summary>
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Visibility test"));
        var level = session.Document.Levels[0].Id;
        var wall = session.AddWall(level, new(0, 0), new(4, 0));
        var plan = session.Document.Views.Single(view => view.Kind == ProjectViewKind.Plan && view.LevelId == level);
        var model = session.Document.Views.Single(view => view.Kind == ProjectViewKind.Model3D);
        var elevation = session.Document.Views.Single(view => view.Id == BuiltInProjectViews.NorthElevationId);
        session.SetViewVisibility(plan.Id, [wall], []);
        var updatedPlan = session.Document.Views.Single(view => view.Id == plan.Id);
        check(ViewVisibility.Apply(session.Document, updatedPlan).Walls.Count == 0 &&
              ViewVisibility.Apply(session.Document, model).Walls.Any(item => item.Id == wall),
            "Hiding a wall in one view leaves it visible in another");
        var template = session.CreateViewTemplate(updatedPlan, "Wall visibility")!.Value;
        session.ApplyViewTemplate(template, [model.Id, elevation.Id]);
        check(session.Document.Views.Where(view => view.Id == model.Id || view.Id == elevation.Id)
                .All(view => ViewVisibility.Apply(session.Document, view).Walls.Count == 0),
            "A saved template applies visibility to multiple views");
        session.Undo();
        check(session.Document.Views.Single(view => view.Id == model.Id).HiddenElementIds.Count == 0,
            "Undo restores the previous batch visibility");
        session.Redo();
        var path = Path.Combine(Path.GetTempPath(), $"visibility-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reopened = ProjectFile.Load(path);
            check(reopened.ViewTemplates.Any(item => item.Id == template) &&
                  reopened.Views.Single(view => view.Id == model.Id).HiddenElementIds.Contains(wall),
                "View visibility and templates survive project save and reopen");
        }
        finally { File.Delete(path); }
        RunSameTypeChecks(check);
    }

    /// <summary>Checks Revit-style "select all of the same type" and the batch removal that follows it.</summary>
    static void RunSameTypeChecks(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Same type"));
        var level = session.Document.Levels[0].Id;
        var a = session.AddWall(level, new(0, 0), new(4, 0));
        var b = session.AddWall(level, new(0, 3), new(4, 3));
        var c = session.AddWall(level, new(0, 6), new(4, 6));
        var otherType = BuiltInWallTypes.Exterior300Id;
        var index = session.Document.Walls.FindIndex(wall => wall.Id == c);
        session.Document.Walls[index] = session.Document.Walls[index] with { WallTypeId = otherType };

        var same = ElementTypeSelection.SameType(session.Document, [a]);
        check(same.SetEquals([a, b]), "Select all of the same type picks the walls sharing the wall type and not other types");
        check(ElementTypeSelection.KindOf(session.Document, c) == Guanacaste3D.Core.Navigation.PickedKind.Wall &&
              ElementTypeSelection.KindOf(session.Document, Guid.NewGuid()) is null,
            "An id resolves to its element kind, or to nothing");

        check(session.RemoveElements([.. same]) == 2 && session.Document.Walls.Count == 1 &&
              session.Document.Walls[0].Id == c, "Removing a type selection deletes every member");
        session.Undo();
        check(session.Document.Walls.Count == 3, "Undo restores the whole removed type selection in one step");
    }
}
