using System.Text.Json.Nodes;
using Guanacaste3D.Agent;
using Guanacaste3D.Core;

/// <summary>Checks automatic element tags and automatic dimensions, including their assistant tools.</summary>
static class AnnotationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var document = ModernHouseShowcase.Create();
        var ground = document.Levels.Single(level => level.Role == LevelRole.BaseFloor).Id;
        var tags = ElementTags.Build(document, ground);
        var doors = tags.Where(tag => tag.Category == TagCategory.Door).ToList();
        check(doors.Count == 1 && doors[0].Mark == "P1", "The villa's single ground-floor door is tagged P1");

        var demo = SampleProjects.CreateDemonstrationHouse();
        var demoLevel = demo.Levels[0].Id;
        var demoTags = ElementTags.Build(demo, demoLevel);
        var demoDoorTypes = demo.Openings.Where(o => o.Kind == OpeningKind.Door).Select(o => o.OpeningTypeId).Distinct().Count();
        check(demoTags.Count(tag => tag.Category == TagCategory.Window) == demo.Openings.Count(o => o.Kind == OpeningKind.Window) &&
            demoTags.Where(tag => tag.Category == TagCategory.Door).Select(tag => tag.Mark).Distinct().Count() == demoDoorTypes,
            "Every window is tagged and openings of one type share a mark");

        var wall = demo.Walls.First(w => demo.Openings.Any(o => o.WallId == w.Id));
        var opening = demo.Openings.First(o => o.WallId == wall.Id);
        var tag = demoTags.Single(t => t.ElementId == opening.Id);
        var onAxis = new Point2(wall.Start.X + (wall.End.X - wall.Start.X) / wall.Length * opening.Offset,
            wall.Start.Y + (wall.End.Y - wall.Start.Y) / wall.Length * opening.Offset);
        check(Math.Abs(tag.Position.DistanceTo(onAxis) - (wall.Thickness / 2 + 0.35)) < 1e-6,
            "An opening tag sits beside its host wall at the opening's offset");

        check(ElementTags.Build(demo, demoLevel, new HashSet<TagCategory> { TagCategory.Room }).Count == demo.Rooms.Count(r => r.LevelId == demoLevel),
            "Room tags follow the requested categories");

        var plan = demo.Views.First(view => view.Kind == ProjectViewKind.Plan && view.LevelId == demoLevel);
        var secondPlan = new ProjectView(Guid.NewGuid(), "Planta alternativa", ProjectViewKind.Plan,
            demoLevel, ViewOrientation.None);
        var twoPlanDocument = demo.Clone();
        twoPlanDocument.Views.Add(secondPlan);
        var tagged = new DocumentSession(twoPlanDocument);
        var areaId = tagged.AddArea(demoLevel, "Patio de prueba", new Point2(25, 25), new Point2(30, 29));
        var areas = ElementTags.Build(tagged.Document, demoLevel, new HashSet<TagCategory> { TagCategory.Area });
        var newAreaTag = areas.SingleOrDefault(tag => tag.ElementId == areaId);
        check(newAreaTag is not null && newAreaTag.Category == TagCategory.Area &&
              newAreaTag.Mark.StartsWith("Patio de prueba", StringComparison.Ordinal) &&
              newAreaTag.Position == new Point2(27.5, 27) &&
              newAreaTag.Description.Contains("20"),
            "Area labels are derived from actual geometric area and remain attached to the model element");
        tagged.SetPlanTagVisibility(plan.Id, TagCategory.Room, true);
        tagged.SetPlanTagVisibility(plan.Id, TagCategory.Room, true); // idempotent, no extra undo step
        tagged.SetPlanTagVisibility(plan.Id, TagCategory.Area, true);
        check(tagged.Document.Views.Single(view => view.Id == plan.Id).ShowRoomTags &&
              tagged.Document.Views.Single(view => view.Id == plan.Id).ShowAreaTags,
            "Room and area tags can be enabled independently for one plan view");
        check(tagged.Document.Views.Single(view => view.Id == secondPlan.Id) is
              { ShowRoomTags: false, ShowAreaTags: false },
            "Tag visibility is scoped to the selected plan, even when two plans share a level");
        var rejectedNonPlan = false;
        try { tagged.SetPlanTagVisibility(BuiltInProjectViews.Model3DId, TagCategory.Room, true); }
        catch (InvalidDataException) { rejectedNonPlan = true; }
        check(rejectedNonPlan, "Room tags cannot be enabled on a non-plan view");
        var file = Path.Combine(Path.GetTempPath(), "g3d-plan-tags-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(file, tagged.Document);
            var reopened = ProjectFile.Load(file);
            check(reopened.Views.Single(view => view.Id == plan.Id) is { ShowRoomTags: true, ShowAreaTags: true } &&
                  ElementTags.Build(reopened, demoLevel, new HashSet<TagCategory> { TagCategory.Area })
                      .Any(tag => tag.ElementId == areaId),
                "Room/area tag visibility and associative area identity survive native project reload");
            check(reopened.Views.Single(view => view.Id == secondPlan.Id) is
                  { ShowRoomTags: false, ShowAreaTags: false },
                "Saving and reopening preserves independent tag settings for multiple plan views");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
        }
        tagged.Undo();
        check(tagged.Document.Views.Single(view => view.Id == plan.Id) is { ShowRoomTags: true, ShowAreaTags: false },
            "Undo hides area tags without hiding room tags");
        tagged.Undo();
        check(tagged.Document.Views.Single(view => view.Id == plan.Id) is { ShowRoomTags: false, ShowAreaTags: false },
            "Repeated tag commands do not create extra undo steps");
        tagged.Redo();
        tagged.Redo();
        check(tagged.Document.Views.Single(view => view.Id == plan.Id) is { ShowRoomTags: true, ShowAreaTags: true },
            "Redo restores both plan tag visibility states");

        var session = new DocumentSession(demo);
        var added = session.AutoDimensionLevel(demoLevel);
        var expected = demo.Walls.Count(w => w.LevelId == demoLevel) + demo.Openings.Count;
        check(added == expected && session.Document.Dimensions.Count == expected, "Auto dimension covers every wall and opening");
        check(session.AutoDimensionLevel(demoLevel) == 0, "Auto dimension is idempotent");
        session.Undo();
        check(session.Document.Dimensions.Count == added, "Auto dimension is one undo step (the repeat added nothing)");
        session.Undo();
        check(session.Document.Dimensions.Count == 0, "Undoing auto dimension removes all of them");

        var context = new AgentContext(new DocumentSession(ModernHouseShowcase.Create()));
        var registry = ToolRegistry.CreateDefault();
        string Run(string name, string json) => registry.Find(name)!.Run(context,
            new ToolArguments(JsonNode.Parse(json)!.AsObject(), context.Session));
        var name = context.Session.Document.Levels.Single(level => level.Role == LevelRole.BaseFloor).Name;
        check(Run("get_tags", $$"""{"level":"{{name}}"}""").Contains("\"P1\"") &&
            Run("auto_dimension", $$"""{"level":"{{name}}"}""").StartsWith("Added") &&
            Run("get_area_schedule", "{}").Contains("Total"), "Assistant tools expose tags, auto dimensions and the area table");
    }
}
