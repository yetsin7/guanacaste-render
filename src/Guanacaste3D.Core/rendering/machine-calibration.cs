using System.Text.Json;

namespace Guanacaste3D.Core.Rendering;

/// <summary>A benchmark result remembered for one machine, so the measurement runs once instead of at every start.</summary>
/// <param name="CpuName">Processor the measurement belongs to; a different CPU invalidates it.</param>
/// <param name="LogicalCores">Thread count at measurement time; a change (BIOS, VM limits) invalidates it.</param>
/// <param name="MegaTests">Measured triangle tests per second, in millions, over all cores.</param>
public sealed record MachineCalibration(string CpuName, int LogicalCores, double MegaTests, DateTime MeasuredUtc);

/// <summary>
/// Loads the machine's render benchmark from the user's local folder, measuring it when it is missing, belongs to other
/// hardware or is older than <see cref="MaxAge"/> (drivers, power plans and thermal paste change speed over time).
/// </summary>
public static class MachineCalibrationStore
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>
    /// %LOCALAPPDATA%\Guanacaste3D\render-calibration.json, beside the other per-user preferences. Debug builds run the
    /// benchmark unoptimized and about 30× slower, so they keep their own file instead of poisoning the release result.
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste3D",
#if DEBUG
        "render-calibration.debug.json");
#else
        "render-calibration.json");
#endif

    /// <summary>
    /// Returns the detected hardware with a measured throughput. The measurement (about half a second on all cores) runs on
    /// a worker thread, so calling this from the interface never blocks it.
    /// </summary>
    /// <param name="force">Measure again even when a valid result is stored (the "recalibrate" button).</param>
    public static Task<HardwareProfile> LoadOrMeasureAsync(string? path = null, bool force = false) => Task.Run(() =>
    {
        path ??= DefaultPath;
        var hardware = HardwareProfile.Detect();
        if (!force && Read(path) is { } stored && IsValidFor(stored, hardware, DateTime.UtcNow))
            return hardware.WithMeasurement(stored.MegaTests);

        var megaTests = HardwareProfile.Measure();
        Write(path, new MachineCalibration(hardware.CpuName, hardware.LogicalCores, megaTests, DateTime.UtcNow));
        return hardware.WithMeasurement(megaTests);
    });

    /// <summary>A stored result applies when it was taken on the same processor and thread count within <see cref="MaxAge"/>.</summary>
    public static bool IsValidFor(MachineCalibration stored, HardwareProfile hardware, DateTime nowUtc) =>
        stored.MegaTests > 0 &&
        stored.LogicalCores == hardware.LogicalCores &&
        string.Equals(stored.CpuName, hardware.CpuName, StringComparison.Ordinal) &&
        nowUtc - stored.MeasuredUtc < MaxAge &&
        stored.MeasuredUtc <= nowUtc;

    private static MachineCalibration? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<MachineCalibration>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>Saves atomically; a failure only means the benchmark runs again next time.</summary>
    private static void Write(string path, MachineCalibration calibration)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(calibration));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
