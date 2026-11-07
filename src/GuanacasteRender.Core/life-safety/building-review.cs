using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>The discipline a review finding belongs to.</summary>
public enum ReviewDiscipline { Architecture, Structure, Electrical, Lighting, Plumbing, LifeSafety, Coordination }

/// <summary>One thing the model still lacks or gets wrong, in the words of a design review, with how many there are.</summary>
public sealed record ReviewFinding(ReviewDiscipline Discipline, string Topic, int Count, string Detail);

/// <summary>
/// Aggregates available preliminary model checks. The findings are not a comprehensive design review or a
/// determination of code compliance; additional checks and professional review remain necessary.
/// </summary>
public static class BuildingReview
{
    /// <summary>Runs available model checks and groups their preliminary findings by discipline.</summary>
    public static IReadOnlyList<ReviewFinding> Review(BuildingDocument document)
    {
        var findings = new List<ReviewFinding>();
        void Add(ReviewDiscipline discipline, string topic, int count, string detail)
        {
            if (count > 0) findings.Add(new(discipline, topic, count, detail));
        }
        var c = CultureInfo.InvariantCulture;
        var rooms = document.Rooms.Count;
        var occupied = LifeSafetyAnalysis.OccupiedStoreys(document);

        // Architecture and structure: the envelope, the frame and the spaces.
        Add(ReviewDiscipline.Architecture, "Storeys without rooms", document.Levels.Count(level =>
                document.Walls.Any(wall => wall.LevelId == level.Id) && document.Rooms.All(room => room.LevelId != level.Id)),
            "Levels with walls but no room annotations: spaces cannot be scheduled, lit or protected.");
        Add(ReviewDiscipline.Architecture, "Storeys without a floor slab", occupied.Count(level =>
                document.Floors.All(floor => floor.LevelId != level.Id || floor.Kind != FloorKind.Floor)),
            "Occupied levels with no floor slab under them.");
        Add(ReviewDiscipline.Architecture, "No roof", document.Floors.Any(floor => floor.Kind == FloorKind.Roof) || occupied.Count == 0 ? 0 : 1,
            "The building has no roof slab.");
        Add(ReviewDiscipline.Structure, "No columns or bearing walls", occupied.Count > 1 && document.Columns.Count == 0 ? 1 : 0,
            "A multi-storey building with no columns.");
        Add(ReviewDiscipline.Structure, "No foundations", document.Columns.Count > 0 && !document.StructuralElements.Any(element =>
                element.Kind is StructuralElementKind.IsolatedFoundation or StructuralElementKind.WallFoundation) ? 1 : 0,
            "Columns stand on nothing: add isolated or wall footings.");

        // Electrical and lighting.
        var electrical = ElectricalAnalysis.Analyze(document);
        Add(ReviewDiscipline.Electrical, "No electrical panel", rooms > 0 && electrical.Panels.Count == 0 ? 1 : 0,
            "The building has no load center.");
        Add(ReviewDiscipline.Electrical, "Devices without a circuit", document.ElectricalDevices.Count(device =>
                device.CircuitId is null && !device.IsFireProtection && !device.IsDirectCurrent &&
                device.Kind is not (ElectricalDeviceKind.Panel or ElectricalDeviceKind.SolarArray)),
            "Receptacles, lights or equipment that no breaker feeds.");
        var circuits = electrical.Panels.SelectMany(panel => panel.Circuits).ToArray();
        Add(ReviewDiscipline.Electrical, "Overloaded or dead circuits", circuits.Count(circuit =>
                circuit.Status is CircuitStatus.Tripped or CircuitStatus.WrongVoltage), "Circuits whose breaker trips or whose voltage is wrong.");
        Add(ReviewDiscipline.Electrical, "Voltage drop over 3 %", circuits.Count(circuit => circuit.VoltageDropPercent > 3),
            "Branch circuits too long for their conductor (NEC 210.19 note).");
        var lighting = LightingAnalysis.Analyze(document, electrical);
        Add(ReviewDiscipline.Lighting, "Rooms without light", lighting.Count(room => room.Status == LightingStatus.Dark),
            "Rooms with no luminaire.");
        Add(ReviewDiscipline.Lighting, "Underlit rooms", lighting.Count(room => room.Status == LightingStatus.Underlit),
            "Rooms below 90 % of the illuminance their use needs.");
        Add(ReviewDiscipline.LifeSafety, "Bedrooms without a smoke alarm", lighting.Count(room => room.NeedsSmokeDetector && !room.HasSmokeDetector),
            "The preliminary screening found sleeping rooms without a modeled smoke alarm.");
        Add(ReviewDiscipline.Lighting, "Rooms without a switch", document.Rooms.Count(room =>
                lighting.Any(item => item.Room.Id == room.Id && item.Luminaires > 0) &&
                !document.ElectricalDevices.Any(device => device.IsSwitch && device.LevelId == room.LevelId &&
                    device.Controls.Any(id => document.ElectricalDevices.Any(light => light.Id == id && light.LevelId == room.LevelId &&
                        LifeSafetyAnalysis.Inside(room, light.Position))))),
            "Lit rooms whose lights no switch controls.");
        Add(ReviewDiscipline.Electrical, "Rooms without receptacles", document.Rooms.Count(room =>
                !IsCirculation(room.Name) && !document.ElectricalDevices.Any(device => device.IsReceptacle && device.LevelId == room.LevelId &&
                    Near(room, device.Position))),
            "Habitable rooms with no receptacle (NEC 210.52).");

        // Plumbing.
        var fixtures = document.StructuralElements.Count(element => PlumbingStandards.Fixture(element) is not null);
        if (fixtures > 0 || occupied.Count > 0)
        {
            var plumbing = PlumbingAnalysis.Analyze(document);
            Add(ReviewDiscipline.Plumbing, "No plumbing fixtures", occupied.Count > 0 && fixtures == 0 ? 1 : 0,
                "No kitchen, bathroom or laundry fixture in the building.");
            Add(ReviewDiscipline.Plumbing, "Fixtures not connected", plumbing.Unserved.Count, "Fixtures missing a supply or a drain system.");
            var failing = plumbing.Systems.SelectMany(system => system.Fixtures).Where(item => item.Status == PlumbingStatus.Fail).ToArray();
            Add(ReviewDiscipline.Plumbing, "Fixtures failing the hydraulic check", failing.Length,
                failing.Select(item => item.Issue).FirstOrDefault(issue => issue.Length > 0) ?? "Pressure, slope or cover below the minimum.");
            Add(ReviewDiscipline.Plumbing, "No water source", fixtures > 0 && !document.PlumbingPoints.Any(point => point.IsColdSource) ? 1 : 0,
                "No water meter, cistern or tank.");
            Add(ReviewDiscipline.Plumbing, "No sanitary outlet", fixtures > 0 && !document.PlumbingPoints.Any(point => point.IsSanitaryOutlet) ? 1 : 0,
                "No sewer connection or septic tank.");
        }

        // Life safety, by rule.
        var safety = LifeSafetyAnalysis.Analyze(document);
        foreach (var group in safety.Issues.GroupBy(issue => issue.Code))
            Add(ReviewDiscipline.LifeSafety, group.Key.ToString(), group.Count(), LifeSafetyText(group.Key, safety));

        // Coordination.
        foreach (var group in ModelAudit.Analyze(document).GroupBy(issue => issue.Code))
            Add(ReviewDiscipline.Coordination, group.Key.ToString(), group.Count(), string.Format(c, "Model audit: {0}.", group.Key));
        return findings;
    }

    /// <summary>A plain-text report: one line per finding, grouped by discipline, or a line saying none remain.</summary>
    public static string Format(IReadOnlyList<ReviewFinding> findings)
    {
        const string scope = "Preliminary model screening only; this does not establish design completeness or code compliance.";
        if (findings.Count == 0) return scope + Environment.NewLine + "No findings in the available checks.";
        return scope + Environment.NewLine + string.Join(Environment.NewLine, findings.Select(finding =>
            string.Create(CultureInfo.InvariantCulture, $"[{finding.Discipline}] {finding.Topic}: {finding.Count} — {finding.Detail}")));
    }

    private static string LifeSafetyText(LifeSafetyCode code, LifeSafetyReport report) => code switch
    {
        LifeSafetyCode.MissingExit => "The modeled stair count is below the preliminary threshold.",
        LifeSafetyCode.NoElevator => "No modeled elevator was found serving this storey under the preliminary threshold.",
        LifeSafetyCode.ElevatorTooSmall => "The modeled hoistway is below the preliminary size threshold.",
        LifeSafetyCode.SprinklerCoverage => report.HighRise
            ? "The preliminary high-rise screening found fewer sprinkler heads than its area threshold."
            : "The preliminary screening found fewer sprinkler heads than its area threshold.",
        LifeSafetyCode.ExtinguisherTravel => "Rooms exceed the preliminary straight-line extinguisher distance.",
        LifeSafetyCode.HoseReach => "Rooms exceed the preliminary straight-line hose distance.",
        LifeSafetyCode.ExitSigns => "Storeys with fewer exit signs than exits.",
        LifeSafetyCode.EmergencyLighting => "Storeys short of emergency lights (one per 150 m²).",
        LifeSafetyCode.PullStations => "Storeys with fewer manual pull stations than exits.",
        LifeSafetyCode.Notification => "Storeys short of horn-strobes (one per 250 m²).",
        _ => code.ToString()
    };

    private static bool IsCirculation(string name) =>
        new[] { "pasillo", "corredor", "escalera", "vestíbulo", "hall", "corridor", "stair", "bodega" }
            .Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static bool Near(Room room, Point2 point) =>
        point.X >= Math.Min(room.FirstCorner.X, room.SecondCorner.X) - 0.2 && point.X <= Math.Max(room.FirstCorner.X, room.SecondCorner.X) + 0.2 &&
        point.Y >= Math.Min(room.FirstCorner.Y, room.SecondCorner.Y) - 0.2 && point.Y <= Math.Max(room.FirstCorner.Y, room.SecondCorner.Y) + 0.2;
}
