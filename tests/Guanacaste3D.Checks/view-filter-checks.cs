using Guanacaste3D.Core;

/// <summary>Checks view filters: the rule parser, which walls a rule hides, and that only the view that has it is affected.</summary>
static class ViewFilterChecks
{
    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var document = session.Document;
        var model = document.Views.First(view => view.Kind == ProjectViewKind.Model3D);
        var plan = document.Views.First(view => view.Kind == ProjectViewKind.Plan);
        var walls = document.Walls.Count;

        check(ViewFilterParser.TryParse("espesor > 0.25") is { Property: ViewFilterProperty.Thickness, Operator: ViewFilterOperator.Greater, Value: "0.25" } &&
              ViewFilterParser.TryParse("TIPO contiene cobogó") is { Property: ViewFilterProperty.WallType, Operator: ViewFilterOperator.Contains } &&
              ViewFilterParser.TryParse("height < 2,5") is { Property: ViewFilterProperty.Height, Operator: ViewFilterOperator.Less },
            "Rules are read in Spanish or English, with a decimal comma as well as a point");
        check(ViewFilterParser.TryParse("") is null && ViewFilterParser.TryParse("espesor") is null &&
              ViewFilterParser.TryParse("color es rojo") is null && ViewFilterParser.TryParse("espesor contiene 3") is null &&
              ViewFilterParser.TryParse("altura > mucho") is null && ViewFilterParser.TryParse("tipo > 2") is null &&
              ViewFilterParser.TryParse("altura > NaN") is null && ViewFilterParser.TryParse("altura > Infinity") is null,
            "Text that is not a rule, or whose operator or value does not suit the property, is not read");

        var thickest = document.Walls.Max(wall => wall.Thickness);
        var thin = document.Walls.Count(wall => wall.Thickness < thickest - 0.005);
        var filter = ViewFilterParser.TryParse(FormattableString.Invariant($"espesor < {thickest - 0.005:0.####}"))!;
        session.SetViewFilters(model.Id, [filter]);
        var filtered = ViewVisibility.Apply(session.Document, session.Document.Views.First(view => view.Id == model.Id));
        check(thin > 0 && filtered.Walls.Count == walls - thin && filtered.Walls.All(wall => wall.Thickness >= thickest - 0.005),
            $"A filter on thickness hides the {thin} thinner walls of the house and keeps the {filtered.Walls.Count} others");
        check(filtered.Openings.All(opening => filtered.Walls.Any(wall => wall.Id == opening.WallId)),
            "The openings of the hidden walls go with them");
        check(ViewVisibility.Apply(session.Document, session.Document.Views.First(view => view.Id == plan.Id)).Walls.Count == walls &&
              session.Document.Walls.Count == walls,
            "Another view, and the model itself, keep every wall");

        var typeName = document.WallTypes.First(type => type.Id == document.Walls[0].WallTypeId).Name;
        var byType = ViewFilterParser.TryParse($"tipo es {typeName}")!;
        var sameType = document.Walls.Count(wall => wall.WallTypeId == document.Walls[0].WallTypeId);
        session.SetViewFilters(model.Id, [byType]);
        check(ViewVisibility.Apply(session.Document, session.Document.Views.First(view => view.Id == model.Id)).Walls.Count == walls - sameType,
            $"A filter on the type \"{typeName}\" hides its {sameType} walls");

        session.SetViewFilters(model.Id, [filter, byType]);
        var both = ViewVisibility.Apply(session.Document, session.Document.Views.First(view => view.Id == model.Id)).Walls.Count;
        check(both <= walls - Math.Max(thin, sameType), "Two filters together hide what either of them matches");

        check(Throws(() => session.SetViewFilters(model.Id, [new ViewFilter("x", ViewFilterProperty.Thickness, ViewFilterOperator.Contains, "1")])) &&
              Throws(() => session.SetViewFilters(model.Id, Enumerable.Repeat(filter, DocumentSession.MaximumViewFilters + 1).ToArray())),
            "A rule that makes no sense, or more rules than a view can hold, is refused");

        var file = Path.Combine(Path.GetTempPath(), $"filters-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.Views.First(view => view.Id == model.Id).Filters.Count == 2, "The filters of a view survive saving and opening the project");

        session.Undo();
        check(session.Document.Views.First(view => view.Id == model.Id).Filters.Count == 1, "Undo takes the last change of filters back");
        session.SetViewFilters(model.Id, []);
        check(ViewVisibility.Apply(session.Document, session.Document.Views.First(view => view.Id == model.Id)).Walls.Count == walls, "Without filters the view shows every wall again");
    }
}
