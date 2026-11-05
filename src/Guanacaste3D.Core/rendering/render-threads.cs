namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Room in .NET's thread pool for the renderer. Path tracing splits every pass into many short tasks for all the cores, but the pool
/// starts with as many threads as cores, a few of which are always taken by the render's own tasks and the window; it then adds new
/// threads about one every half second, so for minutes most cores sat idle (3 of 16 busy in a movie). Raising the pool's starting
/// size once removes the wait at no cost: idle threads cost nothing.
/// </summary>
internal static class RenderThreads
{
    private static int _prepared;

    /// <summary>Makes sure the pool starts with at least twice as many worker threads as cores (done once).</summary>
    public static void EnsureCapacity()
    {
        if (Interlocked.Exchange(ref _prepared, 1) == 1) return;
        ThreadPool.GetMinThreads(out var workers, out var completion);
        var wanted = Math.Max(4, Environment.ProcessorCount * 2);
        if (workers < wanted) ThreadPool.SetMinThreads(wanted, completion);
    }
}
