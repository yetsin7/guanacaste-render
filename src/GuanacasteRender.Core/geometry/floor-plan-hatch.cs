namespace Guanacaste3D.Core;

/// <summary>Drafting appearance of a slab in plan: a base color and the pattern drawn over it.</summary>
public readonly record struct FloorPlanHatch(string Color, WallFillPattern Pattern);

/// <summary>
/// Chooses how a floor slab is hatched in plan, the way architectural drawings tell finishes apart: tile as a grid,
/// exposed concrete and open roofs (terraces) as diagonal lines, and any other floor as a plain tint.
/// </summary>
public static class FloorPlanHatchRules
{
    /// <summary>Returns the plan hatch of a slab; toposolids keep their own contour rendering.</summary>
    public static FloorPlanHatch For(FloorSlab slab) => slab.Kind switch
    {
        FloorKind.Roof => new("#C9D3DB", WallFillPattern.Diagonal),
        FloorKind.Ceiling => new("#E6E6E6", WallFillPattern.Solid),
        _ => slab.Finish switch
        {
            FinishKind.Ceramic or FinishKind.Tile => new("#D8E4EC", WallFillPattern.Crosshatch),
            FinishKind.ExposedConcrete => new("#CFCFCF", WallFillPattern.Diagonal),
            FinishKind.Asphalt => new("#9A9A9A", WallFillPattern.Solid),
            FinishKind.Grass => new("#BFD8A8", WallFillPattern.Solid),
            _ => new("#D7E3DB", WallFillPattern.Solid)
        }
    };
}
