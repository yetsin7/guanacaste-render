using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the modern villa sample and the water surface it introduces.</summary>
static class ModernHouseChecks
{
    public static void Run(Action<bool, string> check)
    {
        var document = ModernHouseShowcase.Create();
        document.Validate();
        check(document.Levels.Count == 3 && document.Levels.Any(level => level.Name == "Planta alta"),
            "Modern villa has a ground level, an upper level and the roof datum");

        var groundSlab = document.Floors.Single(f => f.Kind == FloorKind.Floor && f.Name == "Concrete Slab 200 mm" &&
            document.Levels.Single(l => l.Id == f.LevelId).Elevation == 0);
        var upperSlab = document.Floors.Single(f => f.Kind == FloorKind.Floor && f.Name == "Concrete Slab 200 mm" &&
            document.Levels.Single(l => l.Id == f.LevelId).Elevation > 0);
        check(upperSlab.Boundary.Max(p => p.X) > groundSlab.Boundary.Max(p => p.X) + 2 &&
            upperSlab.Boundary.Min(p => p.Y) < groundSlab.Boundary.Min(p => p.Y) - 1,
            "The upper volume cantilevers past the ground floor to the east and south");
        check(document.StructuralElements.Count(e => e.Kind == StructuralElementKind.CurtainWall) == 3,
            "Glazed pavilion and upper band use three curtain walls");

        var mesh = BuildingMesher.Build(document, includeGround: true);
        check(mesh.TriangleKinds.Contains(SurfaceKind.Water), "The reflecting pool meshes as water");
        check(mesh.TriangleKinds.Contains(SurfaceKind.Glass) && mesh.TriangleKinds.Contains(SurfaceKind.Foliage),
            "The villa meshes glass and vegetation");

        var water = MaterialLibrary.ForKind(SurfaceKind.Water);
        check(water.Roughness < 0.1f && !water.IsTransmissive && Math.Abs(water.Ior - 1.33f) < 0.01f,
            "Water is a smooth, glossy dielectric that mirrors its surroundings");

        var pool = document.Floors.Single(f => f.Kind == FloorKind.Water);
        check(pool.ElevationOffset < 0 && pool.CalculateArea() > 50, "The pool surface sits below the paving");
        check(BudgetCalculator.Build(document, UnitPriceList.Default()).Total > 0, "The villa can be priced");

        var session = new DocumentSession(document);
        session.Undo();
        check(!session.CanUndo, "A freshly opened sample has no undo history");
    }
}

/// <summary>Checks that the assistant can build the villa's key pieces through its tools.</summary>
static class ModernHouseToolChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new Guanacaste3D.Core.DocumentSession(Guanacaste3D.Core.ArchitecturalLibrary.CreateProject("Prueba"));
        var context = new Guanacaste3D.Agent.AgentContext(session);
        var registry = Guanacaste3D.Agent.ToolRegistry.CreateDefault();
        var level = session.Document.Levels[0].Name;
        string Run(string tool, string json) => registry.Find(tool)!.Run(context,
            new Guanacaste3D.Agent.ToolArguments(System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject(), session));
        Run("add_curtain_wall", $$"""{"level":"{{level}}","x1":0,"y1":0,"x2":8,"y2":0}""");
        Run("add_pool", $$"""{"level":"{{level}}","x":0,"y":-12,"width":10,"depth":5}""");
        Run("add_planting", $$"""{"level":"{{level}}","x":-4,"y":2,"species":"palm"}""");
        var document = session.Document;
        check(document.StructuralElements.Count(e => e.Kind == Guanacaste3D.Core.StructuralElementKind.CurtainWall) == 1 &&
            document.Floors.Count(f => f.Kind == Guanacaste3D.Core.FloorKind.Water) == 1 &&
            document.StructuralElements.Count(e => e.Kind == Guanacaste3D.Core.StructuralElementKind.Planting) == 1,
            "Assistant tools add curtain walls, pools and planting");
    }
}
