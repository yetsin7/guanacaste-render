using System.Runtime;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Returns memory to the system right after a heavy build. Reading a large model or building an acceleration structure
/// leaves hundreds of megabytes of temporary arrays on the large-object heap, and the runtime, seeing plenty of free RAM,
/// would keep them for a long time. Collecting once at the end of each build keeps the application's footprint close
/// to what it actually needs, which matters on computers that are also running Revit or AutoCAD.
/// </summary>
public static class MemoryTrim
{
    private static long _lastTicks;

    /// <summary>Collects and compacts the heap, at most once every 1.5 seconds.</summary>
    public static void AfterHeavyBuild()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastTicks);
        if (now - last < 1500 || Interlocked.CompareExchange(ref _lastTicks, now, last) != last) return;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
