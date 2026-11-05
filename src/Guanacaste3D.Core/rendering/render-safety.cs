using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Guanacaste3D.Core.Rendering;

/// <summary>A snapshot of physical memory, in bytes.</summary>
public readonly record struct MemoryStatus(long TotalBytes, long AvailableBytes)
{
    /// <summary>Fraction of physical memory in use, 0–1.</summary>
    public double Load => TotalBytes > 0 ? 1 - (double)AvailableBytes / TotalBytes : 0;
}

/// <summary>
/// Keeps rendering from hurting the computer. Before a render it fits the image size to the memory that is really
/// free; while it runs, a watchdog stops the render, keeping the image so far, if memory runs short, so the application
/// and the rest of the system never reach the point of failing.
/// </summary>
public static class RenderSafety
{
    /// <summary>Fraction of the free memory a render may plan to use.</summary>
    public const double PlanningShare = 0.55;

    /// <summary>System memory load above which a render is stopped.</summary>
    public const double StopLoad = 0.93;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    /// <summary>Reads physical memory from the operating system; falls back to the runtime's own view elsewhere.</summary>
    public static MemoryStatus ReadMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status)) return new MemoryStatus((long)status.TotalPhysical, (long)status.AvailablePhysical);
        }
        var info = GC.GetGCMemoryInfo();
        return new MemoryStatus(info.TotalAvailableMemoryBytes, Math.Max(0, info.TotalAvailableMemoryBytes - info.MemoryLoadBytes));
    }

    /// <summary>Bytes a render of this size is expected to need on top of the scene itself.</summary>
    public static long EstimateBytes(int width, int height, long sceneTriangles, long sceneBytes)
    {
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (sceneTriangles < 0) throw new ArgumentOutOfRangeException(nameof(sceneTriangles));
        if (sceneBytes < 0) throw new ArgumentOutOfRangeException(nameof(sceneBytes));

        // 66 bytes of render accumulators + 16 bytes of auxiliary image data per pixel.
        // Saturate instead of wrapping a large imported scene into a negative "safe" estimate.
        var fixedCost = sceneTriangles > (long.MaxValue - sceneBytes) / 80
            ? long.MaxValue : sceneBytes + sceneTriangles * 80;
        var pixels = (long)width * height;
        return pixels > (long.MaxValue - fixedCost) / 82
            ? long.MaxValue : fixedCost + pixels * 82;
    }

    /// <summary>
    /// Returns the largest size not exceeding the requested one that fits the free memory, keeping the aspect ratio.
    /// <see cref="FitResult.Reduced"/> tells the interface to explain why the image is smaller than asked.
    /// </summary>
    public static FitResult FitToMemory(int width, int height, long sceneTriangles, long sceneBytes, MemoryStatus memory)
    {
        var budget = (long)(memory.AvailableBytes * PlanningShare);
        var w = Math.Clamp(width, 16, 8192);
        var h = Math.Clamp(height, 16, 8192);
        var reduced = w != width || h != height;
        while (EstimateBytes(w, h, sceneTriangles, sceneBytes) > budget && w > 96 && h > 64)
        {
            w = (int)(w * 0.85);
            h = (int)(h * 0.85);
            reduced = true;
        }
        var fits = EstimateBytes(w, h, sceneTriangles, sceneBytes) <= budget;
        return new FitResult(w, h, reduced, fits, EstimateBytes(w, h, sceneTriangles, sceneBytes));
    }

    public readonly record struct FitResult(int Width, int Height, bool Reduced, bool Fits, long EstimatedBytes);

    /// <summary>
    /// Watches memory on a timer and calls <paramref name="onExceeded"/> once when the system is nearly out of
    /// memory or this process has grown past its own limit.
    /// </summary>
    public sealed class Watchdog : IDisposable
    {
        private readonly Timer _timer;
        private readonly long _processLimit;
        private readonly Action _onExceeded;
        private int _fired;

        public Watchdog(long processLimitBytes, Action onExceeded, TimeSpan? interval = null)
        {
            _processLimit = processLimitBytes;
            _onExceeded = onExceeded;
            var period = interval ?? TimeSpan.FromMilliseconds(750);
            _timer = new Timer(_ => Check(), null, period, period);
        }

        public bool Fired => Volatile.Read(ref _fired) != 0;

        private void Check()
        {
            if (Fired) return;
            var exceeded = ReadMemory().Load > StopLoad;
            if (!exceeded)
            {
                using var process = Process.GetCurrentProcess();
                exceeded = _processLimit > 0 && process.PrivateMemorySize64 > _processLimit;
            }
            if (exceeded && Interlocked.Exchange(ref _fired, 1) == 0) _onExceeded();
        }

        public void Dispose() => _timer.Dispose();
    }
}
