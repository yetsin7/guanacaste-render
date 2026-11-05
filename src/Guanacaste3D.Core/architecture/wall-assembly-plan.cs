namespace Guanacaste3D.Core;

/// <summary>Construction-aware, metric wall-plan dimensions. All lengths are in meters.</summary>
public readonly record struct WallAssemblyPlan(
    WallConstruction Construction,
    double TotalThickness,
    double CoreThickness,
    double LeftPlaster,
    double LeftFine,
    double RightPlaster,
    double RightFine,
    double StudSpacing)
{
    public double LeftFinish => LeftPlaster + LeftFine;
    public double RightFinish => RightPlaster + RightFine;
    public bool IsMasonry => Construction is WallConstruction.Block or WallConstruction.Brick;
    public bool IsGypsum => Construction == WallConstruction.Gypsum;
}

/// <summary>
/// Resolves wall layer positions without depending on the WPF renderer.
/// Existing saved wall types without finish data keep their historical solid appearance.
/// </summary>
public static class WallAssemblyPlanRules
{
    public const double Stud16Inches = 0.4064;
    public const double Stud24Inches = 0.6096;

    public static WallAssemblyPlan For(WallType type, double actualThickness)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!double.IsFinite(actualThickness) || actualThickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(actualThickness));

        var left = type.LeftRepelloThickness + type.LeftFineThickness;
        var right = type.RightRepelloThickness + type.RightFineThickness;
        if (left + right >= actualThickness)
            throw new ArgumentException("Wall finishes cannot consume the entire wall core.", nameof(type));
        return new WallAssemblyPlan(type.Construction, actualThickness, actualThickness - left - right,
            type.LeftRepelloThickness, type.LeftFineThickness,
            type.RightRepelloThickness, type.RightFineThickness, type.GypsumStudSpacing);
    }

    /// <summary>Coordinates of the longitudinal layer boundaries, relative to the wall centreline.</summary>
    public static IReadOnlyList<double> LayerOffsets(WallAssemblyPlan assembly)
    {
        var left = -assembly.TotalThickness / 2;
        var right = assembly.TotalThickness / 2;
        return new[]
        {
            left + assembly.LeftFine,
            left + assembly.LeftFine + assembly.LeftPlaster,
            right - assembly.RightFine - assembly.RightPlaster,
            right - assembly.RightFine
        }.Where(offset => offset > left + 1e-8 && offset < right - 1e-8)
         .Distinct().OrderBy(offset => offset).ToArray();
    }

    /// <summary>Stud centre positions including both ends, with the pitch never exceeding the chosen spacing.</summary>
    public static IReadOnlyList<double> StudPositions(double length, double spacing)
    {
        if (!double.IsFinite(length) || length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (!double.IsFinite(spacing) || spacing < 0.15 || spacing > 1.2)
            throw new ArgumentOutOfRangeException(nameof(spacing));

        // Check the floating-point count before casting to int or allocating an array.
        // Otherwise an extreme finite length can overflow the cast or exhaust memory.
        const int maximumIntervals = 1_000_000;
        var requestedIntervals = Math.Ceiling(length / spacing);
        if (!double.IsFinite(requestedIntervals) || requestedIntervals > maximumIntervals)
            throw new ArgumentOutOfRangeException(nameof(length), "Wall requires too many stud intervals.");
        var spans = Math.Max(1, (int)requestedIntervals);
        return Enumerable.Range(0, spans + 1).Select(index => length * index / spans).ToArray();
    }
}
