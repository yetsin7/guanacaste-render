using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Microsoft.Win32;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Graphics adapter manufacturer, as far as the driver name reveals it.</summary>
public enum GpuVendor { Unknown, Nvidia, Amd, Intel, Other }

/// <summary>One graphics adapter reported by the operating system.</summary>
/// <param name="Name">Driver description, e.g. "NVIDIA GeForce RTX 4050 Laptop GPU".</param>
/// <param name="DedicatedMemoryBytes">Dedicated video memory; 0 when the adapter shares system memory.</param>
/// <param name="IsIntegrated">True for adapters that share system memory.</param>
/// <param name="InferredHardwareRayTracing">
/// Guess from the product name only (RTX, Radeon RX 6000+, Arc). Nothing is verified until a GPU backend queries
/// the driver, so this is informational and never gates a feature.
/// </param>
public sealed record GpuInfo(string Name, GpuVendor Vendor, long DedicatedMemoryBytes, bool IsIntegrated,
    bool InferredHardwareRayTracing);

/// <summary>
/// What the machine offers the renderer. Detection uses only the registry and the .NET runtime, so it works
/// without native libraries and never blocks on a driver call.
/// </summary>
public sealed class HardwareProfile
{
    public string CpuName { get; init; } = "CPU";
    public int LogicalCores { get; init; } = Environment.ProcessorCount;
    public bool VectorHardwareAcceleration { get; init; } = Vector.IsHardwareAccelerated;
    /// <summary>Width in bits of the widest SIMD register .NET reports as accelerated (128, 256 or 512).</summary>
    public int VectorBits { get; init; }
    public long TotalMemoryBytes { get; init; }
    public long AvailableMemoryBytes { get; init; }
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    /// <summary>Measured triangle tests per second, in millions, over all cores; 0 when not measured.</summary>
    public double MicrobenchmarkMegaTests { get; init; }
    public bool IsOnBattery { get; init; }

    /// <summary>The most capable adapter: dedicated memory first, then any non-integrated one.</summary>
    public GpuInfo? PrimaryGpu => Gpus.OrderByDescending(gpu => gpu.DedicatedMemoryBytes).ThenBy(gpu => gpu.IsIntegrated)
        .FirstOrDefault();

    /// <summary>
    /// The renderer in this build runs on the CPU, so the GPU is reported but not used. This line is shown to
    /// the user so a claim of GPU acceleration is never implied.
    /// </summary>
    public string BackendSummary =>
        $"CPU SIMD ({LogicalCores} hilos, {VectorBits} bits)" +
        (PrimaryGpu is { } gpu ? $" · GPU detectada: {gpu.Name} (sin usar en esta versión)" : "");

    /// <summary>Reads the machine description; <paramref name="measure"/> adds a ~150 ms CPU microbenchmark.</summary>
    public static HardwareProfile Detect(bool measure = false)
    {
        var memory = GC.GetGCMemoryInfo();
        var profile = new HardwareProfile
        {
            CpuName = ReadCpuName(),
            VectorBits = Vector512.IsHardwareAccelerated ? 512 : Vector256.IsHardwareAccelerated ? 256 :
                Vector128.IsHardwareAccelerated ? 128 : 0,
            TotalMemoryBytes = memory.TotalAvailableMemoryBytes,
            AvailableMemoryBytes = Math.Max(0, memory.TotalAvailableMemoryBytes - memory.MemoryLoadBytes),
            Gpus = ReadGpus(),
            MicrobenchmarkMegaTests = measure ? Measure() : 0,
            IsOnBattery = ReadOnBattery()
        };
        return profile;
    }

    /// <summary>A copy of this profile carrying a throughput measured earlier (for example, read from the calibration file).</summary>
    public HardwareProfile WithMeasurement(double megaTests) => new()
    {
        CpuName = CpuName, LogicalCores = LogicalCores, VectorHardwareAcceleration = VectorHardwareAcceleration,
        VectorBits = VectorBits, TotalMemoryBytes = TotalMemoryBytes, AvailableMemoryBytes = AvailableMemoryBytes,
        Gpus = Gpus, MicrobenchmarkMegaTests = Math.Max(0, megaTests), IsOnBattery = IsOnBattery
    };

    /// <summary>
    /// Median of three short runs. A single run is easily skewed by turbo ramp-up or a background task, and the median
    /// throws away one outlier in either direction without making the measurement noticeably longer.
    /// </summary>
    public static double Measure()
    {
        var runs = new double[3];
        for (var i = 0; i < runs.Length; i++) runs[i] = MeasureTriangleTests(TimeSpan.FromMilliseconds(150));
        Array.Sort(runs);
        return runs[1];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>True only when Windows reports the machine unplugged; desktops and unknown states count as mains power.</summary>
    private static bool ReadOnBattery()
    {
        try
        {
            return OperatingSystem.IsWindows() && GetSystemPowerStatus(out var status) && status.AcLineStatus == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    private static string ReadCpuName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key?.GetValue("ProcessorNameString") is string name && name.Trim().Length > 0)
                    return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return "CPU";
    }

    private static IReadOnlyList<GpuInfo> ReadGpus()
    {
        var adapters = new List<GpuInfo>();
        if (!OperatingSystem.IsWindows()) return adapters;
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return adapters;
            foreach (var subKeyName in classKey.GetSubKeyNames().Where(name => name.Length == 4 && name.All(char.IsDigit)))
            {
                using var key = classKey.OpenSubKey(subKeyName);
                if (key?.GetValue("DriverDesc") is not string name) continue;
                if (name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)) continue;
                long memory = key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long value => value,
                    byte[] bytes when bytes.Length >= 8 => BitConverter.ToInt64(bytes, 0),
                    int value => value,
                    _ => 0
                };
                adapters.Add(Classify(name, memory));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return adapters;
    }

    /// <summary>Turns a driver name and memory size into a <see cref="GpuInfo"/> (exposed for tests).</summary>
    public static GpuInfo Classify(string name, long dedicatedMemoryBytes)
    {
        var upper = name.ToUpperInvariant();
        var vendor = upper.Contains("NVIDIA") || upper.Contains("GEFORCE") || upper.Contains("QUADRO") ? GpuVendor.Nvidia
            : upper.Contains("AMD") || upper.Contains("RADEON") ? GpuVendor.Amd
            : upper.Contains("INTEL") ? GpuVendor.Intel
            : GpuVendor.Unknown;
        var integrated = dedicatedMemoryBytes <= 0 && (vendor == GpuVendor.Intel || upper.Contains("RADEON GRAPHICS") ||
                                                       upper.Contains("VEGA") || upper.Contains("UHD") || upper.Contains("IRIS"));
        var rayTracing = vendor switch
        {
            GpuVendor.Nvidia => upper.Contains("RTX"),
            GpuVendor.Intel => upper.Contains("ARC(") || upper.Contains("ARC "),
            GpuVendor.Amd => System.Text.RegularExpressions.Regex.IsMatch(upper, @"RX\s*[6-9]\d{3}"),
            _ => false
        };
        return new GpuInfo(name, vendor, Math.Max(0, dedicatedMemoryBytes), integrated, rayTracing);
    }

    /// <summary>Counts Möller–Trumbore tests per second over all cores for about <paramref name="duration"/>.</summary>
    private static double MeasureTriangleTests(TimeSpan duration)
    {
        var timer = Stopwatch.StartNew();
        long total = 0;
        Parallel.For(0, Environment.ProcessorCount, worker =>
        {
            var state = (uint)(worker * 2654435761u + 12345);
            float Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state >> 8) * (1f / 16777216f);
            }
            var v0 = new Vector3(-1, -1, 4);
            var e1 = new Vector3(2, 0, 0);
            var e2 = new Vector3(0, 2, 0);
            long count = 0;
            var sink = 0f;
            while (timer.Elapsed < duration)
            {
                for (var i = 0; i < 4096; i++)
                {
                    var direction = Vector3.Normalize(new Vector3(Next() - 0.5f, Next() - 0.5f, 1));
                    var p = Vector3.Cross(direction, e2);
                    var determinant = Vector3.Dot(e1, p);
                    var t = -v0;
                    var u = Vector3.Dot(t, p) / determinant;
                    var q = Vector3.Cross(t, e1);
                    var v = Vector3.Dot(direction, q) / determinant;
                    sink += u + v;
                }
                count += 4096;
            }
            GC.KeepAlive(sink);
            Interlocked.Add(ref total, count);
        });
        return total / timer.Elapsed.TotalSeconds / 1e6;
    }
}
