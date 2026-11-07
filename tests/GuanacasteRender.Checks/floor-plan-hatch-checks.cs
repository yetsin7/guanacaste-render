using Guanacaste3D.Core;

/// <summary>Checks that plan hatches tell floor finishes, terraces and ceilings apart.</summary>
static class FloorPlanHatchChecks
{
    public static void Run(Action<bool, string> check)
    {
        FloorSlab Slab(FloorKind kind, FinishKind finish) =>
            new(Guid.NewGuid(), Guid.NewGuid(), "Slab", [new(0, 0), new(4, 0), new(4, 3)], 0.2) { Kind = kind, Finish = finish };

        check(FloorPlanHatchRules.For(Slab(FloorKind.Floor, FinishKind.Ceramic)).Pattern == WallFillPattern.Crosshatch,
            "Ceramic floors are hatched as a tile grid in plan");
        check(FloorPlanHatchRules.For(Slab(FloorKind.Floor, FinishKind.ExposedConcrete)).Pattern == WallFillPattern.Diagonal,
            "Exposed concrete floors are hatched diagonally in plan");
        check(FloorPlanHatchRules.For(Slab(FloorKind.Roof, FinishKind.None)).Pattern == WallFillPattern.Diagonal,
            "Roofs and terraces are hatched diagonally in plan");
        check(FloorPlanHatchRules.For(Slab(FloorKind.Floor, FinishKind.None)).Pattern == WallFillPattern.Solid,
            "A floor without finish keeps a plain tint");
    }
}
