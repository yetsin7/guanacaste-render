using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Exports circuit and dimmer behavior from the source application.</summary>
internal static class ElectricalSimulationReference
{
    /// <summary>Creates powered, switched, dimmed, disconnected, overloaded, and wrong-voltage lights.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var document = BuildingDocument.Create("Electrical simulation reference");
        var level = document.Levels[0].Id;
        var panel = Id(1);
        var offPanel = Id(2);
        var overloadedPanel = Id(3);
        var solarPanel = Id(4);
        document.ElectricalDevices.Add(new ElectricalDevice(panel, level, ElectricalDeviceKind.Panel, "Main panel", new Point2(0, 0), 1.5));
        document.ElectricalDevices.Add(new ElectricalDevice(offPanel, level, ElectricalDeviceKind.Panel, "Open panel", new Point2(2, 0), 1.5) { IsOn = false });
        document.ElectricalDevices.Add(new ElectricalDevice(overloadedPanel, level, ElectricalDeviceKind.Panel, "Overloaded panel", new Point2(4, 0), 1.5) { MainBreakerAmps = 1 });
        document.ElectricalDevices.Add(new ElectricalDevice(solarPanel, level, ElectricalDeviceKind.Panel, "Solar panel", new Point2(6, 0), 1.5) { MainBreakerAmps = 1 });
        document.ElectricalCircuits.Add(new ElectricalCircuit(Id(11), panel, 1, "Lighting"));
        document.ElectricalCircuits.Add(new ElectricalCircuit(Id(12), panel, 2, "Open breaker") { BreakerOn = false });
        document.ElectricalCircuits.Add(new ElectricalCircuit(Id(13), panel, 3, "Wrong voltage") { Voltage = 240 });
        document.ElectricalCircuits.Add(new ElectricalCircuit(Id(14), panel, 4, "Overloaded") { BreakerAmps = 1 });
        document.ElectricalCircuits.Add(new ElectricalCircuit(Id(15), offPanel, 1, "Panel off"));
        document.ElectricalCircuits.Add(new ElectricalCircuit(Id(16), overloadedPanel, 1, "Main breaker overload"));
        document.ElectricalCircuits.Add(new ElectricalCircuit(Id(17), solarPanel, 1, "Solar house load"));
        document.ElectricalCircuits.Add(new ElectricalCircuit(Id(18), solarPanel, 2, "Inverter") { Voltage = 240 });
        var circuits = new[] { Id(11), Id(11), Id(11), Id(12), Id(13), Id(14), Id(15) };
        for (var index = 0; index < circuits.Length; index++)
            document.ElectricalDevices.Add(new ElectricalDevice(Id(30 + index), level, ElectricalDeviceKind.CeilingFixture,
                $"Light {index + 1}", new Point2(index * 3, 4), 2.7)
                { CircuitId = circuits[index], Watts = index == 5 ? 1000 : 18 });
        document.ElectricalDevices.Add(new ElectricalDevice(Id(40), level, ElectricalDeviceKind.Downlight, "Main overload light", new Point2(22, 4), 2.7)
            { CircuitId = Id(16), Watts = 300 });
        document.ElectricalDevices.Add(new ElectricalDevice(Id(41), level, ElectricalDeviceKind.Downlight, "Solar house light", new Point2(25, 4), 2.7)
            { CircuitId = Id(17), Watts = 200 });
        document.ElectricalDevices.Add(new ElectricalDevice(Id(42), level, ElectricalDeviceKind.Inverter, "Inverter", new Point2(28, 4), 1.4)
            { CircuitId = Id(18), Watts = 500 });
        document.StructuralElements.Add(new StructuralElement(Id(70), level, StructuralElementKind.Component, "Floor lamp",
            new Point2(31, 4), new Point2(31, 4), 0.4, 1.65, 0) { Depth = 0.4, ComponentShape = ResidentialComponentShape.FloorLamp });
        document.ElectricalDevices.Add(new ElectricalDevice(Id(50), level, ElectricalDeviceKind.Dimmer, "Dimmer", new Point2(0, 2), 1.2)
            { DimLevel = 0.4, Controls = [Id(30), Id(70)] });
        document.ElectricalDevices.Add(new ElectricalDevice(Id(51), level, ElectricalDeviceKind.Switch, "Open switch", new Point2(3, 2), 1.2)
            { IsOn = false, Controls = [Id(31)] });
        for (var index = 0; index < 2; index++)
            document.ElectricalDevices.Add(new ElectricalDevice(Id(52 + index), level, ElectricalDeviceKind.ThreeWaySwitch,
                $"Three-way {index + 1}", new Point2(6 + index, 2), 1.2) { IsOn = false, Controls = [Id(32)] });
        var projectPath = Path.Combine(folder, "electrical-simulation.gr");
        NativeDocumentStore.SaveProject(projectPath, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(projectPath), includeGround: false, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, "electrical-simulation.refmesh"), mesh);
        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, "electrical-simulation.scales")));
        for (var triangle = 0; triangle < mesh.TriangleCount; triangle++) writer.Write(mesh.EmissionOf(triangle));
        var scene = new RenderSceneBuilder().AddBuilding(mesh).Build();
        if (scene.DroppedTriangles != 0) throw new InvalidOperationException("The reference contains invalid triangles.");
        using var emissions = new BinaryWriter(File.Create(Path.Combine(folder, "electrical-simulation.emissions")));
        foreach (var materialIndex in scene.TriangleMaterial)
        {
            var emission = scene.Materials[materialIndex].Emission;
            emissions.Write(emission.X);
            emissions.Write(emission.Y);
            emissions.Write(emission.Z);
        }
        Console.WriteLine($"Wrote {document.ElectricalDevices.Count} devices and {mesh.TriangleCount} reference triangles.");
    }

    /// <summary>Creates stable identifiers for reference elements.</summary>
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
}
