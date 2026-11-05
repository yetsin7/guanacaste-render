using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Lightweight UI-thread telemetry for the CPU path tracer; does not install performance counters or drivers.</summary>
public partial class RenderPanel
{
    private const int TelemetryPoints = 60;
    private readonly Queue<double> _cpuSamples = new();
    private readonly Queue<double> _ramSamples = new();
    private readonly Queue<double> _gpuSamples = new();
    private DispatcherTimer? _renderTelemetryTimer;
    private Process? _measuredProcess;
    private TimeSpan _previousCpuTime;
    private long _previousCpuTimestamp;
    private long _previousTelemetryTimestamp;
    private TimeSpan? _estimatedRemaining;

    /// <summary>Elapsed wall clock time, measured by the render session's Stopwatch, not projected from sampling speed.</summary>
    private static string RenderClock(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100}";
    }

    private void StartRenderTelemetry()
    {
        _estimatedRemaining = null;
        _cpuSamples.Clear();
        _ramSamples.Clear();
        _gpuSamples.Clear();
        CpuLine.Points.Clear();
        RamLine.Points.Clear();
        GpuLine.Points.Clear();
        CpuUsageText.Text = "—";
        RamUsageText.Text = "—";
        RenderElapsedText.Text = RenderClock(TimeSpan.Zero);
        RenderRemainingText.Text = "Estimando…";
        _measuredProcess?.Dispose();
        _measuredProcess = Process.GetCurrentProcess();
        _measuredProcess.Refresh();
        _previousCpuTime = _measuredProcess.TotalProcessorTime;
        _previousCpuTimestamp = Stopwatch.GetTimestamp();
        _previousTelemetryTimestamp = 0;

        _renderTelemetryTimer ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _renderTelemetryTimer.Tick -= RefreshRenderTelemetry;
        _renderTelemetryTimer.Tick += RefreshRenderTelemetry;
        _renderTelemetryTimer.Start();
        RenderTelemetryPanel.Visibility = Visibility.Visible;
        RenderProgressPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Keep the final telemetry graph visible until the user leaves the rendered image.</summary>
    private void StopRenderTelemetry(TimeSpan? finalElapsed = null)
    {
        _renderTelemetryTimer?.Stop();
        if (finalElapsed is { } elapsed) RenderElapsedText.Text = RenderClock(elapsed);
        _measuredProcess?.Dispose();
        _measuredProcess = null;
    }

    private void RefreshRenderTelemetry(object? sender, EventArgs e)
    {
        if (!_rendering) return;
        if (_session?.Tracer is not null)
            RenderElapsedText.Text = RenderClock(_session.RenderElapsed);
        RenderRemainingText.Text = _session?.IsPaused == true
            ? "En pausa"
            : _estimatedRemaining is { } remaining
                ? "≈ " + UiText.Duration(remaining)
                : "Estimando…";

        // CPU and memory polling at most once per second avoids measurable overhead on slower laptops.
        var now = Stopwatch.GetTimestamp();
        if (_previousTelemetryTimestamp != 0 &&
            (now - _previousTelemetryTimestamp) / (double)Stopwatch.Frequency < 1.0)
            return;
        _previousTelemetryTimestamp = now;
        try
        {
            if (_measuredProcess is null) return;
            _measuredProcess.Refresh();
            var cpuTime = _measuredProcess.TotalProcessorTime;
            var seconds = Math.Max(0.001, (now - _previousCpuTimestamp) / (double)Stopwatch.Frequency);
            var usedSeconds = Math.Max(0, (cpuTime - _previousCpuTime).TotalSeconds);
            var cpuPercent = Math.Clamp(usedSeconds / seconds / Math.Max(1, Environment.ProcessorCount) * 100.0, 0, 100);
            _previousCpuTime = cpuTime;
            _previousCpuTimestamp = now;

            var memory = RenderSafety.ReadMemory();
            var ramPercent = Math.Clamp(memory.Load * 100, 0, 100);
            var privateAppMegabytes = _measuredProcess.WorkingSet64 / 1048576.0;

            CpuUsageText.Text = $"{cpuPercent:0}%";
            RamUsageText.Text = $"{ramPercent:0}% · {privateAppMegabytes:0} MB";
            Push(_cpuSamples, cpuPercent);
            Push(_ramSamples, ramPercent);
            // GRE is a CPU-only renderer. This is *renderer GPU usage*, not an OS-wide GPU metric.
            Push(_gpuSamples, 0);
            Draw(CpuLine, _cpuSamples);
            Draw(RamLine, _ramSamples);
            Draw(GpuLine, _gpuSamples);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or
                                    System.ComponentModel.InvalidEnumArgumentException)
        {
            CpuUsageText.Text = "—";
            RamUsageText.Text = "—";
        }
    }

    private static void Push(Queue<double> history, double percent)
    {
        history.Enqueue(Math.Clamp(percent, 0, 100));
        while (history.Count > TelemetryPoints) history.Dequeue();
    }

    private static void Draw(Polyline line, Queue<double> samples)
    {
        const double width = 120, height = 24;
        var points = new PointCollection();
        var index = 0;
        foreach (var percent in samples)
        {
            points.Add(new Point(index * width / (TelemetryPoints - 1),
                height - 1 - Math.Clamp(percent, 0, 100) / 100 * (height - 2)));
            index++;
        }
        line.Points = points;
    }
}
