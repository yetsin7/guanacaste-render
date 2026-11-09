using System.Diagnostics;
using System.Windows.Threading;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Writes the automatic recovery copy shortly after every change, off the interface thread. Each change restarts a
/// short countdown, so a burst of edits makes one copy; a copy that takes longer to write lengthens the wait, so a
/// very large project never keeps the machine busy writing. A change made while a copy is being written makes one more
/// copy right after it. The project is read as a snapshot: a transaction always builds a new document, so the one being
/// written is never modified under it.
/// </summary>
internal sealed class AutosaveScheduler
{
    private static readonly TimeSpan MinimumWait = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private readonly Func<(BuildingDocument Document, string? Path)?> _current;
    private readonly object _gate = new();
    private bool _saving;
    private bool _again;
    private TimeSpan _lastCost;

    /// <summary>Creates a scheduler that asks <paramref name="current"/> for what to protect, or null when nothing needs it.</summary>
    public AutosaveScheduler(Dispatcher dispatcher, Func<(BuildingDocument Document, string? Path)?> current)
    {
        _dispatcher = dispatcher;
        _current = current;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = MinimumWait };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            SaveInBackground();
        };
    }

    /// <summary>Notes that the project changed: the copy is written once the changes pause.</summary>
    public void Touch()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromTicks(Math.Max(MinimumWait.Ticks, _lastCost.Ticks * 2));
        _timer.Start();
    }

    /// <summary>Stops writing copies, as when the window is closing on purpose.</summary>
    public void Stop() => _timer.Stop();

    /// <summary>Writes the copy now and waits for it. Used when the program is about to die, so it never throws.</summary>
    public void SaveNow()
    {
        _timer.Stop();
        try
        {
            if (_current() is { } snapshot) AutoRecovery.Save(snapshot.Document, snapshot.Path);
        }
        catch (Exception exception)
        {
            CrashReporter.Log("emergency save", exception);
        }
    }

    private void SaveInBackground()
    {
        if (_current() is not { } snapshot) return;
        lock (_gate)
        {
            if (_saving)
            {
                _again = true;
                return;
            }
            _saving = true;
        }

        Task.Run(() =>
        {
            try
            {
                var clock = Stopwatch.StartNew();
                AutoRecovery.Save(snapshot.Document, snapshot.Path);
                _lastCost = clock.Elapsed;
            }
            catch (Exception exception)
            {
                CrashReporter.Log("autosave", exception);
            }
            finally
            {
                bool again;
                lock (_gate)
                {
                    _saving = false;
                    again = _again;
                    _again = false;
                }
                if (again && !_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(SaveInBackground);
            }
        });
    }
}
