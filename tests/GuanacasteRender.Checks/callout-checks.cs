using System.Text.Json.Nodes;
using Guanacaste3D.Agent;
using Guanacaste3D.Core;

/// <summary>Checks callouts (persistent detail regions) and view templates that carry display settings.</summary>
static class CalloutChecks
{
    public static void Run(Action<bool, string> check)
    {
        var demo = SampleProjects.CreateDemonstrationHouse();
        var level = demo.Levels[0].Id;
        var session = new DocumentSession(demo);

        var id = session.AddCallout(level, "  ", new PlanBounds(8, 6, 2, 1));
        var callout = session.Document.Callouts.Single();
        check(callout.Name == "Detalle 1" && callout.Region == new PlanBounds(2, 1, 8, 6),
            "A callout gets a default name and a normalized region");

        var thrown = false;
        try { session.AddCallout(level, "Muy chico", new PlanBounds(0, 0, 0.1, 0.1)); }
        catch (InvalidDataException) { thrown = true; }
        check(thrown && session.Document.Callouts.Count == 1, "A callout smaller than the minimum is rejected atomically");

        thrown = false;
        try { session.AddCallout(Guid.NewGuid(), "Sin nivel", new PlanBounds(0, 0, 3, 3)); }
        catch (InvalidDataException) { thrown = true; }
        check(thrown, "A callout on a missing level is rejected");

        var door = session.Document.Openings.First(o => o.Kind == OpeningKind.Door);
        var around = session.AddCalloutAround(door.Id, 1.0, "Puerta principal");
        var region = session.Document.Callouts.Single(c => c.Id == around).Region;
        var bounds = PlanDimensions.Bounds(session.Document, door.Id)!.Value.Bounds;
        check(Math.Abs(region.MinX - (bounds.MinX - 1)) < 1e-9 && Math.Abs(region.MaxY - (bounds.MaxY + 1)) < 1e-9,
            "A callout around an element grows its outline by the margin");

        var path = Path.Combine(Path.GetTempPath(), $"g3-callout-{Guid.NewGuid():N}.g3");
        try
        {
            ProjectFile.Save(path, session.Document);
            var loaded = ProjectFile.Load(path);
            check(loaded.Callouts.Count == 2 && loaded.Callouts.Any(c => c.Name == "Puerta principal" && c.Region == region),
                "Callouts survive save and load");
        }
        finally { if (File.Exists(path)) File.Delete(path); }

        session.RemoveCallout(id);
        check(session.Document.Callouts.Count == 1, "A callout can be removed");
        session.Undo();
        check(session.Document.Callouts.Count == 2, "Removing a callout is undoable");

        var view = session.Document.Views.First(v => v.Kind == ProjectViewKind.Model3D);
        var style = new ViewGraphicsSettings { Shadows = true, ShowEdges = false, SunPreset = "Afternoon" };
        session.SetViewGraphics(view.Id, style);
        var templateId = session.CreateViewTemplate(session.Document.Views.Single(v => v.Id == view.Id), "Presentación")!.Value;
        var plan = session.Document.Views.First(v => v.Kind == ProjectViewKind.Plan);
        var ownCrop = new ViewGraphicsSettings { CropActive = true, Crop = new ViewCropRegion(0.1, 0.1, 0.5, 0.5) };
        session.SetViewGraphics(plan.Id, ownCrop);
        session.ApplyViewTemplate(templateId, [plan.Id]);
        var applied = session.Document.Views.Single(v => v.Id == plan.Id).Graphics;
        check(applied is { Shadows: true, ShowEdges: false, SunPreset: "Afternoon", CropActive: true } && applied.Crop == ownCrop.Crop,
            "A view template carries display settings but each view keeps its own crop");

        session.RemoveViewTemplate(templateId);
        check(session.Document.ViewTemplates.All(t => t.Id != templateId) &&
            session.Document.Views.Single(v => v.Id == plan.Id).Graphics is { Shadows: true },
            "Deleting a template keeps what views already took from it");

        var context = new AgentContext(new DocumentSession(SampleProjects.CreateDemonstrationHouse()));
        var registry = ToolRegistry.CreateDefault();
        var levelName = context.Session.Document.Levels[0].Name;
        var result = registry.Find("add_callout")!.Run(context, new ToolArguments(
            JsonNode.Parse($$"""{"level":"{{levelName}}","x1":0,"y1":0,"x2":4,"y2":4,"name":"Cocina"}""")!.AsObject(), context.Session));
        check(context.Session.Document.Callouts.Single().Name == "Cocina" && result.Contains("id"),
            "The assistant can mark a callout");
    }
}
