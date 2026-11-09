namespace Guanacaste3D.Core;

/// <summary>Health of a circuit or panel: within its rating, above the 80 % continuous limit, or tripped.</summary>
public enum CircuitStatus
{
    /// <summary>No device is connected.</summary>
    Empty,
    Ok,
    /// <summary>Above 80 % of the breaker: acceptable for short loads, not for continuous ones (NEC 210.20).</summary>
    Warning,
    /// <summary>The breaker was switched off by hand.</summary>
    Off,
    /// <summary>Overloaded: the breaker has tripped and the circuit is dead.</summary>
    Tripped,
    /// <summary>
    /// A 240 V appliance on a 120 V circuit (it will not start) or a 120 V device on a 240 V circuit (it would burn):
    /// the circuit is held dead until it is corrected.
    /// </summary>
    WrongVoltage
}

/// <summary>The result for one circuit.</summary>
public sealed record CircuitReport(
    ElectricalCircuit Circuit,
    IReadOnlyList<ElectricalDevice> Devices,
    double LoadWatts,
    double Current,
    double Utilization,
    string ConductorAwg,
    double ConductorSquareMillimeters,
    double LengthMeters,
    double VoltageDropPercent,
    CircuitStatus Status,
    IReadOnlyList<Point2> Route)
{
    /// <summary>Whether the circuit carries power.</summary>
    public bool Energized => Status is CircuitStatus.Ok or CircuitStatus.Warning;

    /// <summary>Power fed back into the panel by inverters on this circuit, in watts (included in <see cref="Current"/>).</summary>
    public double GenerationWatts { get; init; }
}

/// <summary>The result for one panel.</summary>
public sealed record PanelReport(ElectricalDevice Panel, IReadOnlyList<CircuitReport> Circuits, double LoadWatts,
    double Current, double Utilization, CircuitStatus Status)
{
    /// <summary>Inverter output fed into the panel, in watts.</summary>
    public double GenerationWatts { get; init; }

    /// <summary>
    /// The 120 % rule (NEC 705.12(B)(3)): the breakers feeding the bus — the main plus every inverter breaker — may add up
    /// to no more than 120 % of the bus rating (taken as the main's rating).
    /// </summary>
    public bool BackfeedWithinLimit { get; init; } = true;
}

/// <summary>
/// Load calculation and live simulation of the electrical system. Each circuit adds the connected load of its devices
/// (lamp watts, 180 VA per receptacle), divides by its voltage for the current, and compares it with the breaker: above
/// 80 % is a warning, above 100 % the breaker trips. The conductor follows from the breaker (NEC 240.4(D)); the wire runs
/// from the panel through the devices in nearest-neighbour order, up and down to the ceiling, and its voltage drop is
/// checked against 3 %. A panel trips its main breaker when the total current passes its rating. Luminaires light when
/// their circuit is live and their switches allow it: single-pole switches and dimmers in series, three-way switches by
/// parity (each one toggles), dimmers scaling the output.
/// </summary>
public sealed class ElectricalAnalysis
{
    private const double CeilingHeight = 2.7;
    private readonly Dictionary<Guid, CircuitReport> _circuits = [];
    private readonly Dictionary<Guid, ElectricalDevice> _devices;
    private readonly Dictionary<Guid, List<ElectricalDevice>> _controllers = [];
    private readonly HashSet<Guid> _livePanels = [];
    private readonly bool _anyPanel;

    public IReadOnlyList<PanelReport> Panels { get; }

    private ElectricalAnalysis(BuildingDocument document)
    {
        _devices = document.ElectricalDevices.ToDictionary(device => device.Id);
        _anyPanel = document.ElectricalDevices.Any(device => device.Kind == ElectricalDeviceKind.Panel);
        foreach (var device in document.ElectricalDevices.Where(device => device.IsSwitch))
            foreach (var target in device.Controls)
            {
                if (!_controllers.TryGetValue(target, out var list)) _controllers[target] = list = [];
                list.Add(device);
            }

        var panels = new List<PanelReport>();
        foreach (var panel in document.ElectricalDevices.Where(device => device.Kind == ElectricalDeviceKind.Panel))
        {
            var reports = document.ElectricalCircuits.Where(circuit => circuit.PanelId == panel.Id).OrderBy(circuit => circuit.Number)
                .Select(circuit => Report(document, panel, circuit)).ToList();
            var load = reports.Sum(report => report.LoadWatts - report.GenerationWatts);
            var generation = reports.Sum(report => report.GenerationWatts);
            var current = load / 240.0;
            var main = panel.MainBreakerAmps > 0 ? panel.MainBreakerAmps : 100;
            var utilization = current / main;
            var status = !panel.IsOn ? CircuitStatus.Off
                : utilization > 1 ? CircuitStatus.Tripped
                : utilization > 0.8 ? CircuitStatus.Warning
                : reports.Count == 0 ? CircuitStatus.Empty : CircuitStatus.Ok;
            if (status is CircuitStatus.Off or CircuitStatus.Tripped)
                reports = reports.Select(report => report with { Status = report.Status == CircuitStatus.Empty ? CircuitStatus.Empty : status }).ToList();
            else _livePanels.Add(panel.Id);
            foreach (var report in reports) _circuits[report.Circuit.Id] = report;
            var backfeed = reports.Where(report => report.GenerationWatts > 0).Sum(report => report.Circuit.BreakerAmps);
            panels.Add(new PanelReport(panel, reports, load, current, utilization, status)
            {
                GenerationWatts = generation, BackfeedWithinLimit = main + backfeed <= 1.2 * main
            });
        }
        Panels = panels;
    }

    public static ElectricalAnalysis Analyze(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new ElectricalAnalysis(document);
    }

    public CircuitReport? Circuit(Guid circuitId) => _circuits.GetValueOrDefault(circuitId);

    /// <summary>
    /// Whether a device has power: on a live circuit of a live panel. Until the project has a panel, devices are taken
    /// as powered, so lights placed while sketching the design already light the render.
    /// </summary>
    public bool Energized(ElectricalDevice device) => device.CircuitId is Guid circuit
        ? _circuits.TryGetValue(circuit, out var report) && report.Energized
        : !_anyPanel;

    /// <summary>
    /// Light output from 0 (dark) to 1 of a luminaire device or a lighting component. A component with no switch and
    /// no circuit is treated as plugged in and always lit.
    /// </summary>
    public double Brightness(Guid luminaireId)
    {
        var powered = _devices.TryGetValue(luminaireId, out var device) ? Energized(device) : true;
        if (!powered) return 0;
        if (!_controllers.TryGetValue(luminaireId, out var switches) || switches.Count == 0) return 1;
        var level = 1.0;
        var threeWayOff = 0;
        foreach (var control in switches)
        {
            // A switch on a dead circuit still passes the load's own feed; what it cannot do is close an open leg.
            if (control.Kind == ElectricalDeviceKind.ThreeWaySwitch)
            {
                if (!control.IsOn) threeWayOff++;
                continue;
            }
            if (!control.IsOn) return 0;
            if (control.Kind == ElectricalDeviceKind.Dimmer) level *= Math.Clamp(control.DimLevel, 0.1, 1);
        }
        return threeWayOff % 2 == 0 ? level : 0;
    }

    private static CircuitReport Report(BuildingDocument document, ElectricalDevice panel, ElectricalCircuit circuit)
    {
        var devices = document.ElectricalDevices.Where(device => device.CircuitId == circuit.Id).ToList();
        var load = devices.Sum(device => Math.Max(0, device.Watts));
        var current = load / Math.Max(1, circuit.Voltage);
        var utilization = current / Math.Max(1, circuit.BreakerAmps);
        var (awg, area, ohms) = ElectricalStandards.Conductor(circuit.BreakerAmps);

        // The run: from the panel to the nearest device, then on to the nearest remaining one, each time up to the
        // ceiling and down to the next box, plus 10 % for bends and slack.
        var route = new List<Point2> { panel.Position };
        var remaining = devices.ToList();
        var length = 0.0;
        var at = panel.Position;
        var height = panel.Elevation;
        while (remaining.Count > 0)
        {
            var next = remaining.MinBy(device => device.Position.DistanceTo(at))!;
            remaining.Remove(next);
            length += Math.Abs(next.Position.X - at.X) + Math.Abs(next.Position.Y - at.Y) +
                      Math.Abs(CeilingHeight - height) + Math.Abs(CeilingHeight - next.Elevation);
            at = next.Position;
            height = next.Elevation;
            route.Add(at);
        }
        length *= 1.1;
        // Single phase: current flows out and back, so the conductor length counts twice.
        var drop = 2 * length / 1000 * ohms * current / Math.Max(1, circuit.Voltage) * 100;
        var twoPole = circuit.Voltage >= 200;
        var status = devices.Count == 0 ? CircuitStatus.Empty
            : !circuit.BreakerOn ? CircuitStatus.Off
            : devices.Any(device => device.Needs240 != twoPole) ? CircuitStatus.WrongVoltage
            : utilization > 1 ? CircuitStatus.Tripped
            : utilization > 0.8 ? CircuitStatus.Warning
            : CircuitStatus.Ok;
        return new CircuitReport(circuit, devices, load, current, utilization, awg, area, length, drop, status, route)
        {
            GenerationWatts = devices.Where(device => device.IsGenerator).Sum(device => Math.Max(0, device.Watts))
        };
    }
}
