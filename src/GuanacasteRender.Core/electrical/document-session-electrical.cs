namespace Guanacaste3D.Core;

/// <summary>Electrical design edits; each public method is one undoable transaction.</summary>
public sealed partial class DocumentSession
{
    /// <summary>Adds a device with its standard mounting height and load; luminaires hang at <paramref name="ceiling"/>.</summary>
    public Guid AddElectricalDevice(Guid levelId, ElectricalDeviceKind kind, string name, Point2 position, double facing = 0,
        double? ceiling = null)
    {
        var id = Guid.NewGuid();
        var elevation = kind == ElectricalDeviceKind.WaterPump ? 0
            : ElectricalStandards.MountsOnWall(kind) ? ElectricalStandards.MountingHeight(kind) : ceiling ?? WallTopOf(levelId);
        // An air conditioner is sized for the room it is in, and its condenser goes outside the wall behind it.
        var capacity = 0.0;
        Point2? outdoor = null;
        if (kind == ElectricalDeviceKind.MiniSplit)
        {
            var room = CoolingAnalysis.RoomAt(Document, levelId, position);
            capacity = room is null ? 12000 : ElectricalStandards.CoolingCapacityFor(CoolingAnalysis.Area(room));
            var thickness = Document.Walls.Where(wall => wall.LevelId == levelId)
                .Select(wall => (wall.Thickness, Distance: WallDistance(wall, position)))
                .Where(item => item.Distance < 0.6).OrderBy(item => item.Distance).Select(item => item.Thickness)
                .DefaultIfEmpty(0.20).First();
            outdoor = new Point2(position.X - Math.Cos(facing) * (thickness + 0.45), position.Y - Math.Sin(facing) * (thickness + 0.45));
        }
        // A solar array lies on the roof under it, tilted with the slope and facing downhill; with no roof it stands on a
        // ground rack tilted 10° to the south, the best fixed tilt at Costa Rica's latitude.
        var tilt = 0.0;
        if (kind == ElectricalDeviceKind.SolarArray)
            (elevation, tilt, facing) = SolarGeometry.Mounting(Document, levelId, position);
        Commit("Transaction.AddElectricalDevice", document => document.ElectricalDevices.Add(
            new ElectricalDevice(id, levelId, kind, name, position, elevation)
            {
                PanelCount = kind == ElectricalDeviceKind.SolarArray ? 10 : 0,
                PanelWatts = kind == ElectricalDeviceKind.SolarArray ? ElectricalStandards.PanelRating : 0,
                Tilt = tilt,
                Facing = facing,
                Watts = kind == ElectricalDeviceKind.MiniSplit ? ElectricalStandards.CoolingWatts(capacity) : ElectricalStandards.DefaultWatts(kind),
                MainBreakerAmps = kind == ElectricalDeviceKind.Panel ? 100 : 0,
                CoolingBtu = capacity,
                OutdoorPosition = outdoor
            }));
        return id;
    }

    /// <summary>Changes an air conditioner's capacity; its electrical load follows.</summary>
    public void SetCoolingCapacity(Guid deviceId, double btu)
    {
        if (!ElectricalStandards.CoolingCapacities.Contains(btu)) throw new InvalidDataException("Invalid electrical device.");
        Update("Transaction.SetCoolingCapacity", deviceId, existing => existing.Kind != ElectricalDeviceKind.MiniSplit
            ? throw new InvalidDataException("Invalid electrical device.")
            : existing with { CoolingBtu = btu, Watts = ElectricalStandards.CoolingWatts(btu) });
    }

    /// <summary>Sets how many panels a solar array has.</summary>
    public void SetPanelCount(Guid deviceId, int count)
    {
        if (count is < 1 or > 500) throw new InvalidDataException("Invalid electrical device.");
        Update("Transaction.SetPanelCount", deviceId, existing => existing.Kind != ElectricalDeviceKind.SolarArray
            ? throw new InvalidDataException("Invalid electrical device.")
            : existing with { PanelCount = count });
    }

    /// <summary>Sets an inverter's AC rating in watts.</summary>
    public void SetInverterRating(Guid deviceId, double watts)
    {
        if (!ElectricalStandards.InverterRatings.Contains(watts)) throw new InvalidDataException("Invalid electrical device.");
        Update("Transaction.SetInverterRating", deviceId, existing => existing.Kind != ElectricalDeviceKind.Inverter
            ? throw new InvalidDataException("Invalid electrical device.")
            : existing with { Watts = watts });
    }

    /// <summary>Sets a circuit to 120 V (one pole) or 240 V (two poles).</summary>
    public void SetCircuitVoltage(Guid circuitId, double voltage)
    {
        if (voltage is not (120 or 240)) throw new InvalidDataException("Invalid circuit.");
        Commit("Transaction.SetCircuitVoltage", document =>
        {
            var index = document.ElectricalCircuits.FindIndex(circuit => circuit.Id == circuitId);
            if (index < 0) throw new InvalidDataException("The circuit does not exist.");
            document.ElectricalCircuits[index] = document.ElectricalCircuits[index] with { Voltage = voltage };
        });
    }

    private static double WallDistance(Wall wall, Point2 point)
    {
        var length = Math.Max(wall.Length, 1e-9);
        var dx = (wall.End.X - wall.Start.X) / length;
        var dy = (wall.End.Y - wall.Start.Y) / length;
        var along = Math.Clamp((point.X - wall.Start.X) * dx + (point.Y - wall.Start.Y) * dy, 0, length);
        return point.DistanceTo(new Point2(wall.Start.X + dx * along, wall.Start.Y + dy * along));
    }

    /// <summary>Removes a device, its circuits if it is a panel, and every switch link to it.</summary>
    public void RemoveElectricalDevice(Guid deviceId)
    {
        if (Document.ElectricalDevices.All(device => device.Id != deviceId)) return;
        Commit("Transaction.RemoveElectricalDevice", document =>
        {
            var circuits = document.ElectricalCircuits.Where(circuit => circuit.PanelId == deviceId).Select(circuit => circuit.Id).ToHashSet();
            document.ElectricalCircuits.RemoveAll(circuit => circuits.Contains(circuit.Id));
            document.ElectricalDevices.RemoveAll(device => device.Id == deviceId);
            for (var index = 0; index < document.ElectricalDevices.Count; index++)
            {
                var device = document.ElectricalDevices[index];
                if (device.Controls.Contains(deviceId) || device.CircuitId is Guid circuit && circuits.Contains(circuit))
                    document.ElectricalDevices[index] = device with
                    {
                        Controls = device.Controls.Where(id => id != deviceId).ToList(),
                        CircuitId = device.CircuitId is Guid c && circuits.Contains(c) ? null : device.CircuitId
                    };
            }
        });
    }

    /// <summary>Flips a switch (or a panel's main breaker); the lights it controls follow at once.</summary>
    public void ToggleSwitch(Guid deviceId)
    {
        var device = Document.ElectricalDevices.FirstOrDefault(candidate => candidate.Id == deviceId)
            ?? throw new InvalidDataException("The electrical device does not exist.");
        if (!device.IsSwitch && device.Kind != ElectricalDeviceKind.Panel) return;
        Update("Transaction.ToggleSwitch", deviceId, existing => existing with { IsOn = !existing.IsOn });
    }

    /// <summary>Sets a dimmer's output from 10 % to 100 %.</summary>
    public void SetDimLevel(Guid deviceId, double level) =>
        Update("Transaction.SetDimLevel", deviceId, existing => existing with { DimLevel = Math.Clamp(level, 0.1, 1) });

    /// <summary>Sets a device's connected load in watts.</summary>
    public void SetDeviceLoad(Guid deviceId, double watts)
    {
        if (!double.IsFinite(watts) || watts is < 0 or > 50000) throw new InvalidDataException("Invalid electrical device.");
        Update("Transaction.SetDeviceLoad", deviceId, existing => existing with { Watts = watts });
    }

    /// <summary>Links a switch to a luminaire or lighting component, or unlinks it when already linked. Returns the new state.</summary>
    public bool ToggleSwitchLink(Guid switchId, Guid targetId)
    {
        var device = Document.ElectricalDevices.FirstOrDefault(candidate => candidate.Id == switchId && candidate.IsSwitch)
            ?? throw new InvalidDataException("The electrical device does not exist.");
        var linked = !device.Controls.Contains(targetId);
        Update(linked ? "Transaction.LinkSwitch" : "Transaction.UnlinkSwitch", switchId, existing => existing with
        {
            Controls = linked ? [.. existing.Controls, targetId] : existing.Controls.Where(id => id != targetId).ToList()
        });
        return linked;
    }

    /// <summary>Adds the next numbered circuit to a panel: 20 A for receptacles, 15 A for lighting, 30 A at 240 V.</summary>
    public Guid AddCircuit(Guid panelId, string name, double breakerAmps = 15, double voltage = 120)
    {
        if (Document.ElectricalDevices.All(device => device.Id != panelId || device.Kind != ElectricalDeviceKind.Panel))
            throw new InvalidDataException("The electrical device does not exist.");
        var id = Guid.NewGuid();
        var number = Document.ElectricalCircuits.Where(circuit => circuit.PanelId == panelId).Select(circuit => circuit.Number)
            .DefaultIfEmpty(0).Max() + 1;
        Commit("Transaction.AddCircuit", document => document.ElectricalCircuits.Add(
            new ElectricalCircuit(id, panelId, number, name) { BreakerAmps = breakerAmps, Voltage = voltage }));
        return id;
    }

    /// <summary>Feeds a device from a circuit (or disconnects it with null).</summary>
    public void AssignCircuit(Guid deviceId, Guid? circuitId)
    {
        if (circuitId is Guid circuit && Document.ElectricalCircuits.All(candidate => candidate.Id != circuit))
            throw new InvalidDataException("The circuit does not exist.");
        var device = Document.ElectricalDevices.FirstOrDefault(candidate => candidate.Id == deviceId)
            ?? throw new InvalidDataException("The electrical device does not exist.");
        Commit("Transaction.AssignCircuit", document =>
        {
            var index = document.ElectricalDevices.FindIndex(candidate => candidate.Id == deviceId);
            document.ElectricalDevices[index] = device with { CircuitId = circuitId };
            // The first device on an empty circuit sets its voltage: a 240 V appliance makes it a two-pole circuit.
            if (circuitId is Guid target && document.ElectricalDevices.All(other => other.Id == deviceId || other.CircuitId != target))
            {
                var at = document.ElectricalCircuits.FindIndex(candidate => candidate.Id == target);
                document.ElectricalCircuits[at] = document.ElectricalCircuits[at] with { Voltage = device.Needs240 ? 240 : 120 };
            }
        });
    }

    /// <summary>Changes a circuit's breaker: its rating and its handle.</summary>
    public void SetBreaker(Guid circuitId, double amps, bool on)
    {
        if (!ElectricalStandards.BreakerRatings.Contains(amps)) throw new InvalidDataException("Invalid circuit.");
        Commit("Transaction.SetBreaker", document =>
        {
            var index = document.ElectricalCircuits.FindIndex(circuit => circuit.Id == circuitId);
            if (index < 0) throw new InvalidDataException("The circuit does not exist.");
            document.ElectricalCircuits[index] = document.ElectricalCircuits[index] with { BreakerAmps = amps, BreakerOn = on };
        });
    }

    /// <summary>Renames a circuit.</summary>
    public void RenameCircuit(Guid circuitId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidDataException("Invalid circuit.");
        Commit("Transaction.RenameCircuit", document =>
        {
            var index = document.ElectricalCircuits.FindIndex(circuit => circuit.Id == circuitId);
            if (index < 0) throw new InvalidDataException("The circuit does not exist.");
            document.ElectricalCircuits[index] = document.ElectricalCircuits[index] with { Name = name.Trim() };
        });
    }

    /// <summary>Removes a circuit; its devices are left unconnected.</summary>
    public void RemoveCircuit(Guid circuitId)
    {
        if (Document.ElectricalCircuits.All(circuit => circuit.Id != circuitId)) return;
        Commit("Transaction.RemoveCircuit", document =>
        {
            document.ElectricalCircuits.RemoveAll(circuit => circuit.Id == circuitId);
            for (var index = 0; index < document.ElectricalDevices.Count; index++)
                if (document.ElectricalDevices[index].CircuitId == circuitId)
                    document.ElectricalDevices[index] = document.ElectricalDevices[index] with { CircuitId = null };
        });
    }

    /// <summary>Moves an electrical device in plan.</summary>
    public void MoveElectricalDevice(Guid deviceId, double deltaX, double deltaY) =>
        Update("Transaction.MoveElectricalDevice", deviceId, existing => existing with
        {
            Position = new Point2(existing.Position.X + deltaX, existing.Position.Y + deltaY),
            OutdoorPosition = existing.OutdoorPosition is Point2 outdoor ? new Point2(outdoor.X + deltaX, outdoor.Y + deltaY) : null
        });

    private void Update(string transaction, Guid deviceId, Func<ElectricalDevice, ElectricalDevice> change)
    {
        if (Document.ElectricalDevices.All(device => device.Id != deviceId))
            throw new InvalidDataException("The electrical device does not exist.");
        Commit(transaction, document =>
        {
            var index = document.ElectricalDevices.FindIndex(device => device.Id == deviceId);
            document.ElectricalDevices[index] = change(document.ElectricalDevices[index]);
        });
    }
}

/// <summary>Validation rules for the electrical system.</summary>
public static class ElectricalRules
{
    public static void Validate(BuildingDocument document)
    {
        if (document.ElectricalDevices is null || document.ElectricalCircuits is null ||
            document.ElectricalDevices.Count > 20000 || document.ElectricalCircuits.Count > 2000)
            throw new InvalidDataException("Invalid electrical device.");
        var levels = document.Levels.Select(level => level.Id).ToHashSet();
        var circuits = document.ElectricalCircuits.Select(circuit => circuit.Id).ToHashSet();
        var panels = document.ElectricalDevices.Where(device => device.Kind == ElectricalDeviceKind.Panel).Select(device => device.Id).ToHashSet();
        if (document.ElectricalDevices.Select(device => device.Id).Distinct().Count() != document.ElectricalDevices.Count)
            throw new InvalidDataException("Invalid electrical device.");
        foreach (var device in document.ElectricalDevices)
            if (device.Id == Guid.Empty || !levels.Contains(device.LevelId) || !Enum.IsDefined(device.Kind) ||
                string.IsNullOrWhiteSpace(device.Name) || device.Name.Length > 120 ||
                !double.IsFinite(device.Position.X) || !double.IsFinite(device.Position.Y) ||
                !double.IsFinite(device.Elevation) || device.Elevation is < -10 or > 100 || !double.IsFinite(device.Facing) ||
                !double.IsFinite(device.Watts) || device.Watts is < 0 or > 50000 ||
                !double.IsFinite(device.DimLevel) || device.DimLevel is < 0.1 or > 1 ||
                device.CircuitId is Guid circuit && !circuits.Contains(circuit) ||
                device.Controls is null || device.Controls.Count > 200 || device.Controls.Count > 0 && !device.IsSwitch ||
                !double.IsFinite(device.MainBreakerAmps) || device.MainBreakerAmps is < 0 or > 1200 ||
                !double.IsFinite(device.CoolingBtu) || device.CoolingBtu is < 0 or > 200000 ||
                device.PanelCount is < 0 or > 500 || !double.IsFinite(device.PanelWatts) || device.PanelWatts is < 0 or > 1000 ||
                !double.IsFinite(device.Tilt) || device.Tilt is < 0 or > 70 || (device.IsDirectCurrent || device.IsFireProtection) && device.CircuitId is not null ||
                device.OutdoorPosition is Point2 outdoor && (!double.IsFinite(outdoor.X) || !double.IsFinite(outdoor.Y)))
                throw new InvalidDataException("Invalid electrical device.");
        foreach (var circuit in document.ElectricalCircuits)
            if (circuit.Id == Guid.Empty || !panels.Contains(circuit.PanelId) || circuit.Number is < 1 or > 200 ||
                string.IsNullOrWhiteSpace(circuit.Name) || circuit.Name.Length > 80 ||
                !double.IsFinite(circuit.BreakerAmps) || circuit.BreakerAmps is < 1 or > 400 ||
                circuit.Voltage is not (120 or 127 or 208 or 220 or 230 or 240))
                throw new InvalidDataException("Invalid circuit.");
    }
}
