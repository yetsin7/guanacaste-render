using Guanacaste3D.Core;

/// <summary>Checks that a view can hide whole categories, including elements added later.</summary>
static class ViewCategoryChecks
{
    /// <summary>Runs the category visibility scenarios.</summary>
    public static void Run(Action<bool, string> check)
    {
        FilteringByCategory(check);
        SessionTemplatesAndFiles(check);
    }

    private static ProjectView PlanView(BuildingDocument document) =>
        document.Views.First(view => view.Kind == ProjectViewKind.Plan);

    /// <summary>Each category removes only its own elements from the copy a view draws.</summary>
    private static void FilteringByCategory(Action<bool, string> check)
    {
        var document = BuildingDocument.Create();
        var level = document.Levels[0].Id;
        var session = new DocumentSession(document);
        var wall = session.AddWall(level, new Point2(0, 0), new Point2(6, 0));
        session.AddOpening(wall, OpeningKind.Door, offset: 1.5);
        session.AddOpening(wall, OpeningKind.Window, offset: 4.5);
        var model = session.Document.Clone();
        var square = new List<Point2> { new(0, 0), new(5, 0), new(5, 5), new(0, 5) };
        model.Floors.Add(new FloorSlab(Guid.NewGuid(), level, "Floor", square, 0.2));
        model.Floors.Add(new FloorSlab(Guid.NewGuid(), level, "Roof", square, 0.2) { Kind = FloorKind.Roof });
        model.Floors.Add(new FloorSlab(Guid.NewGuid(), level, "Ceiling", square, 0.1) { Kind = FloorKind.Ceiling });
        model.Rooms.Add(new Room(Guid.NewGuid(), level, "1", "Sala", new Point2(0, 0), new Point2(4, 4)));
        var view = PlanView(model);

        Doc Show(params ViewCategory[] hidden) => new(ViewVisibility.Apply(model, view with { HiddenCategories = [.. hidden] }));

        check(Show().Value.Walls.Count == 1 && Show().Value.Floors.Count == 3 && Show().Value.Rooms.Count == 1,
            "A view that hides no category shows everything");
        var noWindows = Show(ViewCategory.Windows).Value;
        check(noWindows.Openings.Count == 1 && noWindows.Openings[0].Kind == OpeningKind.Door && noWindows.Walls.Count == 1,
            "Hiding windows removes only the windows");
        var noDoors = Show(ViewCategory.Doors).Value;
        check(noDoors.Openings.Count == 1 && noDoors.Openings[0].Kind == OpeningKind.Window,
            "Hiding doors removes only the doors");
        var noWalls = Show(ViewCategory.Walls).Value;
        check(noWalls.Walls.Count == 0 && noWalls.Openings.Count == 0,
            "Hiding walls takes their doors and windows with them");
        var noRoofs = Show(ViewCategory.Roofs).Value;
        check(noRoofs.Floors.Count == 2 && noRoofs.Floors.All(floor => floor.Kind != FloorKind.Roof),
            "Hiding roofs leaves floors and ceilings");
        check(Show(ViewCategory.Floors, ViewCategory.Ceilings).Value.Floors.Single().Kind == FloorKind.Roof,
            "Several categories can be hidden together");
        check(Show(ViewCategory.Rooms).Value.Rooms.Count == 0 && model.Rooms.Count == 1,
            "Hiding rooms affects the view's copy, never the project");

        var later = model.Clone();
        later.Walls.Add(new Wall(Guid.NewGuid(), level, new Point2(0, 8), new Point2(6, 8), 0.2, 3));
        check(ViewVisibility.Apply(later, view with { HiddenCategories = [ViewCategory.Walls] }).Walls.Count == 0,
            "A wall added after the category was hidden is hidden too");

        var column = new Column(Guid.NewGuid(), level, new Point2(1, 1), 0.3, 0.3, 3);
        model.Columns.Add(column);
        var withStairs = model.Clone();
        withStairs.StructuralElements.Add(new StructuralElement(Guid.NewGuid(), level, StructuralElementKind.Stair, "S",
            new Point2(0, 0), new Point2(0, 3), 1.0, 3.0, 0));
        var noStairs = ViewVisibility.Apply(withStairs, view with { HiddenCategories = [ViewCategory.Stairs] });
        check(noStairs.StructuralElements.Count == 0 && noStairs.Columns.Count == 1,
            "Hiding stairs leaves columns and the rest of the model");
        check(ViewVisibility.Apply(model, view with { HiddenCategories = [ViewCategory.Columns] }).Columns.Count == 0,
            "Hiding columns removes them");
    }

    /// <summary>The hidden categories are one undoable step, travel with templates and survive saving.</summary>
    private static void SessionTemplatesAndFiles(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var plan = PlanView(session.Document);
        var other = session.Document.Views.First(view => view.Id == BuiltInProjectViews.Model3DId);

        session.SetViewHiddenCategories(plan.Id, [ViewCategory.Windows, ViewCategory.Walls, ViewCategory.Windows]);
        var stored = session.Document.Views.Single(view => view.Id == plan.Id).HiddenCategories;
        check(stored.SequenceEqual([ViewCategory.Walls, ViewCategory.Windows]),
            "A view stores each hidden category once, in a stable order");
        check(session.Document.Views.Single(view => view.Id == other.Id).HiddenCategories.Count == 0,
            "Hiding categories in one view leaves the other views alone");
        session.Undo();
        check(session.Document.Views.Single(view => view.Id == plan.Id).HiddenCategories.Count == 0,
            "Undo shows the categories again");

        session.SetViewHiddenCategories(plan.Id, [ViewCategory.Doors]);
        var template = session.CreateViewTemplate(session.Document.Views.Single(view => view.Id == plan.Id), "Sin puertas");
        session.ApplyViewTemplate(template!.Value, [other.Id]);
        check(session.Document.Views.Single(view => view.Id == other.Id).HiddenCategories.SequenceEqual([ViewCategory.Doors]),
            "A view template carries the hidden categories to the views it is applied to");

        var path = Path.Combine(Path.GetTempPath(), $"g3d-categories-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            check(ProjectFile.Load(path).Views.Single(view => view.Id == plan.Id).HiddenCategories
                    .SequenceEqual([ViewCategory.Doors]),
                "Hidden categories survive saving and reopening the project");
        }
        finally
        {
            File.Delete(path);
        }

        var rejected = false;
        try { session.SetViewHiddenCategories(plan.Id, [(ViewCategory)99]); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "An unknown category is rejected");
        var invalid = session.Document.Clone();
        invalid.Views[0] = invalid.Views[0] with { HiddenCategories = [(ViewCategory)99] };
        rejected = false;
        try { invalid.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "A project with an unknown hidden category fails validation");
    }

    /// <summary>Holds a document so the helper above reads like a table of cases.</summary>
    private readonly record struct Doc(BuildingDocument Value);
}
