namespace Guanacaste3D.Core;

/// <summary>One box of the services schematic: a source, a system or circuit, or something that draws from it.</summary>
public sealed record SchematicNode(Guid Id, string Label, int Column, string Group);

/// <summary>A line from a source to a system, or from a system to what it serves.</summary>
public sealed record SchematicEdge(Guid From, Guid To);

public sealed record MepSchematic(IReadOnlyList<SchematicNode> Nodes, IReadOnlyList<SchematicEdge> Edges)
{
    /// <summary>
    /// The piping and wiring of the project as a flow diagram, left to right: where the service comes from (meters, outlets,
    /// tanks, electrical panels), the systems and circuits it feeds, and the fixtures and devices at the end of them.
    /// </summary>
    public static MepSchematic Build(BuildingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var nodes = new List<SchematicNode>();
        var edges = new List<SchematicEdge>();
        var known = new HashSet<Guid>();

        void Node(Guid id, string label, int column, string group)
        {
            if (known.Add(id)) nodes.Add(new SchematicNode(id, label, column, group));
        }
        string NameOf(Guid id) =>
            document.StructuralElements.FirstOrDefault(item => item.Id == id)?.Name ??
            document.ElectricalDevices.FirstOrDefault(item => item.Id == id)?.Name ?? id.ToString("N")[..6];

        foreach (var system in document.PlumbingSystems)
        {
            var source = document.PlumbingPoints.FirstOrDefault(point => point.Id == system.SourceId);
            if (source is not null) Node(source.Id, source.Name, 0, "Plumbing");
            Node(system.Id, system.Name, 1, "Plumbing");
            if (source is not null) edges.Add(new SchematicEdge(source.Id, system.Id));
            foreach (var fixture in system.Fixtures.Where(id => document.StructuralElements.Any(item => item.Id == id)))
            {
                Node(fixture, NameOf(fixture), 2, "Plumbing");
                edges.Add(new SchematicEdge(system.Id, fixture));
            }
        }
        foreach (var circuit in document.ElectricalCircuits)
        {
            var panel = document.ElectricalDevices.FirstOrDefault(device => device.Id == circuit.PanelId);
            if (panel is not null) Node(panel.Id, panel.Name, 0, "Electrical");
            Node(circuit.Id, $"{circuit.Number}. {circuit.Name} ({circuit.BreakerAmps:0} A)", 1, "Electrical");
            if (panel is not null) edges.Add(new SchematicEdge(panel.Id, circuit.Id));
            foreach (var device in document.ElectricalDevices.Where(item => item.CircuitId == circuit.Id))
            {
                Node(device.Id, device.Name, 2, "Electrical");
                edges.Add(new SchematicEdge(circuit.Id, device.Id));
            }
        }
        // The ducts and trays, which have no systems of their own, are listed as runs so the diagram shows what is installed.
        foreach (var run in document.StructuralElements.Where(item => MepElements.Runs.Contains(item.Kind) || MepElements.Devices.Contains(item.Kind)))
            Node(run.Id, $"{run.Name}", 2, "Mechanical");
        return new MepSchematic(nodes, edges);
    }
}
