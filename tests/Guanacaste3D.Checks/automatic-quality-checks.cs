using Guanacaste3D.Core.Rendering;

/// <summary>Checks that automatic quality respects measured throughput and memory limits.</summary>
static class AutomaticQualityChecks
{
    /// <summary>Compares machines with identical core counts and different measured speeds.</summary>
    public static void Run(Action<bool, string> check)
    {
        var machine = new HardwareProfile { LogicalCores = 16, AvailableMemoryBytes = 16L * 1073741824 };
        check(QualityPlanner.AutomaticQuality(machine.WithMeasurement(300)) == RenderQuality.Draft &&
              QualityPlanner.AutomaticQuality(machine.WithMeasurement(900)) == RenderQuality.Standard &&
              QualityPlanner.AutomaticQuality(machine.WithMeasurement(2000)) == RenderQuality.High,
            "AUTO uses measured throughput instead of core count when calibrated");
        var constrained = new HardwareProfile { LogicalCores = 16, AvailableMemoryBytes = 3L * 1073741824 };
        check(QualityPlanner.AutomaticQuality(constrained.WithMeasurement(4000)) == RenderQuality.Draft &&
              QualityPlanner.AutomaticQuality(machine.WithMeasurement(double.NaN)) == RenderQuality.High,
            "AUTO retains memory limits and falls back when measurement is invalid");
    }
}
