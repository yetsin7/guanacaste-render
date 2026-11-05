namespace Guanacaste3D.Core;

/// <summary>Plumbing design edits; each public method is one undoable transaction.</summary>
public sealed partial class DocumentSession
{
    /// <summary>Places a water meter or a sewer outlet.</summary>
    public Guid AddPlumbingPoint(Guid levelId, PlumbingPointKind kind, string name, Point2 position, string? pumpName = null)
    {
        var id = Guid.NewGuid();
        Commit("Transaction.AddPlumbingPoint", document =>
        {
            Guid? pump = null;
            if (kind == PlumbingPointKind.Cistern)
            {
                // The pump stands beside the tank on its own pad and is wired like any other load.
                pump = Guid.NewGuid();
                document.ElectricalDevices.Add(new ElectricalDevice(pump.Value, levelId, ElectricalDeviceKind.WaterPump,
                    pumpName ?? $"{name} - pump", new Point2(position.X + 1.1, position.Y), 0)
                {
                    Watts = WaterStorage.PumpWatts(1)
                });
            }
            document.PlumbingPoints.Add(new PlumbingPoint(id, levelId, kind, name, position) { PumpDeviceId = pump });
        });
        return id;
    }

    /// <summary>Sets a cistern pump's motor size and pressure; its electrical load follows.</summary>
    public void SetPump(Guid pointId, double horsepower, double kilopascals)
    {
        if (!WaterStorage.PumpSizes.Contains(horsepower) || !double.IsFinite(kilopascals) || kilopascals is < 100 or > 700)
            throw new InvalidDataException("Invalid plumbing point.");
        var point = Document.PlumbingPoints.FirstOrDefault(candidate => candidate.Id == pointId)
            ?? throw new InvalidDataException("The plumbing point does not exist.");
        Commit("Transaction.SetPump", document =>
        {
            var index = document.PlumbingPoints.FindIndex(candidate => candidate.Id == pointId);
            document.PlumbingPoints[index] = point with { PumpHorsepower = horsepower, PumpPressureKPa = kilopascals };
            var pump = document.ElectricalDevices.FindIndex(device => device.Id == point.PumpDeviceId);
            if (pump >= 0) document.ElectricalDevices[pump] = document.ElectricalDevices[pump] with { Watts = WaterStorage.PumpWatts(horsepower) };
        });
    }

    /// <summary>Sets an elevated tank's height above its level, in meters.</summary>
    public void SetTankHeight(Guid pointId, double meters)
    {
        if (!double.IsFinite(meters) || meters is < 1 or > 40) throw new InvalidDataException("Invalid plumbing point.");
        UpdatePoint("Transaction.SetTankHeight", pointId, point => point with { TankHeight = meters });
    }

    /// <summary>Sets a tank's capacity in liters (0 chooses the standard tank the occupants need).</summary>
    public void SetTankCapacity(Guid pointId, double liters)
    {
        if (!double.IsFinite(liters) || liters is < 0 or > 200000) throw new InvalidDataException("Invalid plumbing point.");
        UpdatePoint("Transaction.SetTankCapacity", pointId, point => point with { CapacityLiters = liters });
    }

    /// <summary>Removes a meter or outlet and the systems that start from it.</summary>
    public void RemovePlumbingPoint(Guid pointId)
    {
        if (Document.PlumbingPoints.All(point => point.Id != pointId)) return;
        var pump = Document.PlumbingPoints.First(point => point.Id == pointId).PumpDeviceId;
        Commit("Transaction.RemovePlumbingPoint", document =>
        {
            document.PlumbingPoints.RemoveAll(point => point.Id == pointId);
            if (pump is Guid pumpId) document.ElectricalDevices.RemoveAll(device => device.Id == pumpId);
            document.PlumbingSystems.RemoveAll(system => system.SourceId == pointId);
        });
    }

    /// <summary>Moves a meter or outlet; its systems are routed again from the new position.</summary>
    public void MovePlumbingPoint(Guid pointId, Point2 position) =>
        UpdatePoint("Transaction.MovePlumbingPoint", pointId, point => point with { Position = position });

    /// <summary>Sets the static pressure of the street main at a meter, in kilopascals.</summary>
    public void SetSupplyPressure(Guid pointId, double kilopascals)
    {
        if (!double.IsFinite(kilopascals) || kilopascals is < 50 or > 1000) throw new InvalidDataException("Invalid plumbing point.");
        UpdatePoint("Transaction.SetSupplyPressure", pointId, point => point with { SupplyPressureKPa = kilopascals });
    }

    /// <summary>Sets how deep below the floor the drain leaves the building at an outlet, in meters.</summary>
    public void SetOutletDepth(Guid pointId, double depth)
    {
        if (!double.IsFinite(depth) || depth is < 0.2 or > 5) throw new InvalidDataException("Invalid plumbing point.");
        UpdatePoint("Transaction.SetOutletDepth", pointId, point => point with { InvertDepth = depth });
    }

    /// <summary>Sets how many people a septic tank serves (0 counts two per bedroom).</summary>
    public void SetSepticOccupants(Guid pointId, int occupants)
    {
        if (occupants is < 0 or > 200) throw new InvalidDataException("Invalid plumbing point.");
        UpdatePoint("Transaction.SetSepticOccupants", pointId, point => point with { Occupants = occupants });
    }

    /// <summary>Sets the soil's infiltration rate for a septic tank's drain field, in L/m²·day.</summary>
    public void SetPercolation(Guid pointId, double litersPerSquareMeterDay)
    {
        if (!double.IsFinite(litersPerSquareMeterDay) || litersPerSquareMeterDay is < 5 or > 200) throw new InvalidDataException("Invalid plumbing point.");
        UpdatePoint("Transaction.SetPercolation", pointId, point => point with { Percolation = litersPerSquareMeterDay });
    }

    /// <summary>Sets the design rainfall at a storm outlet, in mm/h.</summary>
    public void SetRainfall(Guid pointId, double millimetersPerHour)
    {
        if (!double.IsFinite(millimetersPerHour) || millimetersPerHour is < 20 or > 400) throw new InvalidDataException("Invalid plumbing point.");
        UpdatePoint("Transaction.SetRainfall", pointId, point => point with { RainfallMmPerHour = millimetersPerHour });
    }

    /// <summary>Starts a system from a meter (cold), a water heater (hot) or an outlet (sanitary).</summary>
    public Guid AddPlumbingSystem(PlumbingSystemKind kind, Guid sourceId, string name)
    {
        CheckSource(kind, sourceId);
        var id = Guid.NewGuid();
        Commit("Transaction.AddPlumbingSystem", document => document.PlumbingSystems.Add(new PlumbingSystem(id, kind, sourceId, name)));
        return id;
    }

    /// <summary>Connects a fixture to a system, or disconnects it when it is already on it; true when now connected.</summary>
    public bool TogglePlumbingFixture(Guid systemId, Guid fixtureId)
    {
        var system = Document.PlumbingSystems.FirstOrDefault(candidate => candidate.Id == systemId)
            ?? throw new InvalidDataException("The plumbing system does not exist.");
        var fixture = Document.StructuralElements.FirstOrDefault(element => element.Id == fixtureId);
        if (system.Kind == PlumbingSystemKind.StormWater)
        {
            if (fixture?.Kind != StructuralElementKind.Downspout) throw new InvalidDataException("The fixture does not use this system.");
        }
        else if (fixture is null || PlumbingStandards.Fixture(fixture) is not { } spec ||
            !(system.Kind switch
            {
                PlumbingSystemKind.ColdWater => spec.NeedsCold || PlumbingStandards.IsWaterHeater(fixture),
                PlumbingSystemKind.HotWater => spec.NeedsHot,
                _ => spec.Drains
            }))
            throw new InvalidDataException("The fixture does not use this system.");
        var connect = !system.Fixtures.Contains(fixtureId);
        Commit(connect ? "Transaction.ConnectFixture" : "Transaction.DisconnectFixture", document =>
        {
            // A fixture is on one system of each kind: connecting it moves it from any other.
            for (var index = 0; index < document.PlumbingSystems.Count; index++)
            {
                var candidate = document.PlumbingSystems[index];
                if (candidate.Kind != system.Kind) continue;
                var fixtures = candidate.Fixtures.Where(id => id != fixtureId).ToList();
                if (connect && candidate.Id == systemId) fixtures.Add(fixtureId);
                document.PlumbingSystems[index] = candidate with { Fixtures = fixtures };
            }
        });
        return connect;
    }

    public void RenamePlumbingSystem(Guid systemId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidDataException("Invalid plumbing system.");
        Commit("Transaction.RenamePlumbingSystem", document =>
        {
            var index = document.PlumbingSystems.FindIndex(system => system.Id == systemId);
            if (index < 0) throw new InvalidDataException("The plumbing system does not exist.");
            document.PlumbingSystems[index] = document.PlumbingSystems[index] with { Name = name.Trim() };
        });
    }

    public void RemovePlumbingSystem(Guid systemId) =>
        Commit("Transaction.RemovePlumbingSystem", document => document.PlumbingSystems.RemoveAll(system => system.Id == systemId));

    /// <summary>
    /// Connects every fixture in one step, as Revit's "Generate layout": cold water from the nearest meter to every
    /// fixture and water heater, hot water from the nearest heater to every fixture that uses it, and the drain from
    /// every fixture to the nearest outlet. Existing systems are replaced. Returns the number of systems made.
    /// </summary>
    public int ConnectPlumbing(Func<PlumbingSystemKind, int, string> name, Func<int, string>? ventName = null,
        Func<int, string>? downspoutName = null)
    {
        ventName ??= number => $"V{number}";
        downspoutName ??= number => $"BP{number}";
        // A house fed from a cistern or an elevated tank takes its cold water from there, not from the meter that fills it.
        var stored = Document.PlumbingPoints.Where(point => point.Kind is PlumbingPointKind.Cistern or PlumbingPointKind.ElevatedTank).ToArray();
        var meters = stored.Length > 0 ? stored : Document.PlumbingPoints.Where(point => point.Kind == PlumbingPointKind.WaterMeter).ToArray();
        var outlets = Document.PlumbingPoints.Where(point => point.IsSanitaryOutlet).ToArray();
        var stormOutlets = Document.PlumbingPoints.Where(point => point.Kind == PlumbingPointKind.StormOutlet).ToArray();
        var heaters = Document.StructuralElements.Where(PlumbingStandards.IsWaterHeater).ToArray();
        var fixtures = Document.StructuralElements.Where(element => PlumbingStandards.Fixture(element) is not null).ToArray();
        if (meters.Length == 0 && outlets.Length == 0 && stormOutlets.Length == 0)
            throw new InvalidDataException("Place a water meter or a sewer outlet first.");
        var elevations = Document.Levels.ToDictionary(level => level.Id, level => level.Elevation);
        double Distance(Point2 a, Guid levelA, Point2 b, Guid levelB) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(elevations[levelA] - elevations[levelB]) * 2;

        var systems = new List<PlumbingSystem>();
        void Build(PlumbingSystemKind kind, IReadOnlyList<(Guid Id, Guid Level, Point2 At)> sources,
            Func<StructuralElement, bool> uses)
        {
            if (sources.Count == 0) return;
            var groups = fixtures.Where(uses)
                .GroupBy(fixture => sources.MinBy(source =>
                    Distance(source.At, source.Level, PlumbingStandards.Connection(fixture), fixture.LevelId)).Id);
            foreach (var group in groups)
                systems.Add(new PlumbingSystem(Guid.NewGuid(), kind, group.Key, name(kind, systems.Count(system => system.Kind == kind) + 1))
                {
                    Fixtures = group.Select(fixture => fixture.Id).ToList()
                });
        }
        Build(PlumbingSystemKind.ColdWater, meters.Select(meter => (meter.Id, meter.LevelId, meter.Position)).ToArray(), fixture =>
            PlumbingStandards.Fixture(fixture)!.NeedsCold || PlumbingStandards.IsWaterHeater(fixture));
        Build(PlumbingSystemKind.HotWater, heaters.Select(heater => (heater.Id, heater.LevelId, PlumbingStandards.Connection(heater))).ToArray(),
            fixture => PlumbingStandards.Fixture(fixture)!.NeedsHot);
        Build(PlumbingSystemKind.Sanitary, outlets.Select(outlet => (outlet.Id, outlet.LevelId, outlet.Position)).ToArray(), fixture =>
            PlumbingStandards.Fixture(fixture)!.Drains);
        // Vent every trap: a fixture with no vent stack within its trap-arm length gets a new stack at its back wall,
        // which then also serves its neighbours (a fixture that drains needs a vent whatever outlet it drains to).
        var vents = Document.PlumbingPoints.Where(point => point.Kind == PlumbingPointKind.VentStack)
            .Select(point => (point.LevelId, point.Position)).ToList();
        var newVents = new List<PlumbingPoint>();
        if (outlets.Length > 0)
            foreach (var fixture in fixtures.Where(fixture => PlumbingStandards.Fixture(fixture)!.Drains)
                         .OrderBy(fixture => fixture.Start.X).ThenBy(fixture => fixture.Start.Y))
            {
                var spec = PlumbingStandards.Fixture(fixture)!;
                var at = PlumbingStandards.Connection(fixture);
                var reach = PlumbingStandards.TrapArmLimit(PlumbingStandards.DrainSize(spec.DrainUnits, spec.MinDrainSize)) * 0.8;
                if (vents.Any(vent => Math.Abs(vent.Position.X - at.X) + Math.Abs(vent.Position.Y - at.Y) <= reach)) continue;
                var count = Document.PlumbingPoints.Count(point => point.Kind == PlumbingPointKind.VentStack) + newVents.Count + 1;
                newVents.Add(new PlumbingPoint(Guid.NewGuid(), fixture.LevelId, PlumbingPointKind.VentStack, ventName(count), at));
                vents.Add((fixture.LevelId, at));
            }
        // Rainwater: each roof gets downspouts enough for its runoff (at least two, at its eave corners), and every
        // downspout drains to the nearest storm outlet.
        var newSpouts = new List<StructuralElement>();
        if (stormOutlets.Length > 0)
        {
            var rainfall = stormOutlets[0].RainfallMmPerHour;
            var spouts = Document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Downspout).ToList();
            foreach (var (roof, outline, area) in SiteDrainage.Roofs(Document))
            {
                var draining = spouts.Concat(newSpouts).Where(spout => SiteDrainage.Drains(outline, spout.Start)).ToList();
                var probe = new StructuralElement(Guid.Empty, roof.LevelId, StructuralElementKind.Downspout, "", default, default, 0.10, 1, 0)
                {
                    Style = ElementStyle.Round
                };
                var needed = Math.Max(2, (int)Math.Ceiling(SiteDrainage.RoofFlow(area, rainfall) / SiteDrainage.LeaderCapacity(probe)));
                var (minX, maxX, minY, maxY) = (outline.Min(p => p.X) + 0.1, outline.Max(p => p.X) - 0.1, outline.Min(p => p.Y) + 0.1, outline.Max(p => p.Y) - 0.1);
                var candidates = new List<Point2> { new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY) };
                // More than four: the middles of the long sides, then their quarter points.
                for (var split = 2; candidates.Count < needed + 4 && split <= 8; split *= 2)
                    for (var part = 1; part < split; part++)
                    {
                        var t = (double)part / split;
                        if (maxX - minX >= maxY - minY)
                        {
                            candidates.Add(new Point2(minX + (maxX - minX) * t, minY));
                            candidates.Add(new Point2(minX + (maxX - minX) * t, maxY));
                        }
                        else
                        {
                            candidates.Add(new Point2(minX, minY + (maxY - minY) * t));
                            candidates.Add(new Point2(maxX, minY + (maxY - minY) * t));
                        }
                    }
                var height = Math.Max(2.4, RoofGeometry.WithOverhang(roof).ElevationOffset);
                foreach (var at in candidates)
                {
                    if (draining.Count >= needed) break;
                    if (draining.Any(spout => spout.Start.DistanceTo(at) < 0.8)) continue;
                    var spout = new StructuralElement(Guid.NewGuid(), roof.LevelId, StructuralElementKind.Downspout,
                        $"{downspoutName(spouts.Count + newSpouts.Count + 1)}", at, at, 0.10, height, 0) { Style = ElementStyle.Round };
                    newSpouts.Add(spout);
                    draining.Add(spout);
                }
            }
            var allSpouts = spouts.Concat(newSpouts).ToList();
            foreach (var group in allSpouts.GroupBy(spout => stormOutlets.MinBy(outlet =>
                         Distance(outlet.Position, outlet.LevelId, spout.Start, spout.LevelId))!.Id))
                systems.Add(new PlumbingSystem(Guid.NewGuid(), PlumbingSystemKind.StormWater, group.Key,
                    name(PlumbingSystemKind.StormWater, systems.Count(system => system.Kind == PlumbingSystemKind.StormWater) + 1))
                {
                    Fixtures = group.Select(spout => spout.Id).ToList()
                });
        }
        Commit("Transaction.ConnectPlumbing", document =>
        {
            document.PlumbingSystems.Clear();
            document.PlumbingSystems.AddRange(systems);
            document.PlumbingPoints.AddRange(newVents);
            document.StructuralElements.AddRange(newSpouts);
        });
        return systems.Count;
    }

    private void CheckSource(PlumbingSystemKind kind, Guid sourceId)
    {
        var valid = kind switch
        {
            PlumbingSystemKind.ColdWater => Document.PlumbingPoints.Any(point => point.Id == sourceId && point.IsColdSource),
            PlumbingSystemKind.HotWater => Document.StructuralElements.Any(element => element.Id == sourceId && PlumbingStandards.IsWaterHeater(element)),
            PlumbingSystemKind.StormWater => Document.PlumbingPoints.Any(point => point.Id == sourceId && point.Kind == PlumbingPointKind.StormOutlet),
            _ => Document.PlumbingPoints.Any(point => point.Id == sourceId && point.IsSanitaryOutlet)
        };
        if (!valid) throw new InvalidDataException("The plumbing source does not exist.");
    }

    private void UpdatePoint(string transaction, Guid pointId, Func<PlumbingPoint, PlumbingPoint> change) =>
        Commit(transaction, document =>
        {
            var index = document.PlumbingPoints.FindIndex(point => point.Id == pointId);
            if (index < 0) throw new InvalidDataException("The plumbing point does not exist.");
            document.PlumbingPoints[index] = change(document.PlumbingPoints[index]);
        });
}

/// <summary>Structural rules of the plumbing data, checked with the rest of the document.</summary>
public static class PlumbingRules
{
    public static void Validate(BuildingDocument document)
    {
        if (document.PlumbingPoints is null || document.PlumbingSystems is null ||
            document.PlumbingPoints.Count > 1000 || document.PlumbingSystems.Count > 2000)
            throw new InvalidDataException("Invalid plumbing point.");
        var levels = document.Levels.Select(level => level.Id).ToHashSet();
        if (document.PlumbingPoints.Select(point => point.Id).Distinct().Count() != document.PlumbingPoints.Count)
            throw new InvalidDataException("Invalid plumbing point.");
        foreach (var point in document.PlumbingPoints)
            if (point.Id == Guid.Empty || !levels.Contains(point.LevelId) || !Enum.IsDefined(point.Kind) ||
                string.IsNullOrWhiteSpace(point.Name) || point.Name.Length > 120 ||
                !double.IsFinite(point.Position.X) || !double.IsFinite(point.Position.Y) ||
                !double.IsFinite(point.SupplyPressureKPa) || point.SupplyPressureKPa is < 50 or > 1000 ||
                !double.IsFinite(point.InvertDepth) || point.InvertDepth is < 0.2 or > 5 ||
                point.Occupants is < 0 or > 200 || !double.IsFinite(point.Percolation) || point.Percolation is < 5 or > 200 ||
                !double.IsFinite(point.RainfallMmPerHour) || point.RainfallMmPerHour is < 20 or > 400 ||
                !double.IsFinite(point.CapacityLiters) || point.CapacityLiters is < 0 or > 200000 ||
                !double.IsFinite(point.PumpPressureKPa) || point.PumpPressureKPa is < 100 or > 700 ||
                !double.IsFinite(point.PumpHorsepower) || point.PumpHorsepower is < 0 or > 50 ||
                !double.IsFinite(point.TankHeight) || point.TankHeight is < 1 or > 40)
                throw new InvalidDataException("Invalid plumbing point.");
        if (document.PlumbingSystems.Select(system => system.Id).Distinct().Count() != document.PlumbingSystems.Count)
            throw new InvalidDataException("Invalid plumbing system.");
        // A system whose heater or fixtures were deleted stays valid: the analysis skips what is missing.
        foreach (var system in document.PlumbingSystems)
            if (system.Id == Guid.Empty || system.SourceId == Guid.Empty || !Enum.IsDefined(system.Kind) ||
                string.IsNullOrWhiteSpace(system.Name) || system.Name.Length > 80 ||
                system.Fixtures is null || system.Fixtures.Count > 10000)
                throw new InvalidDataException("Invalid plumbing system.");
    }
}
