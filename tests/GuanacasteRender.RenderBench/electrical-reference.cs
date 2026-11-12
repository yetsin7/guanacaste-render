using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Produces reference geometry for the migrated electrical catalog.</summary>
internal static class ElectricalReference
{
    /// <summary>Writes two rotations and both switch states for every supported device type.</summary>
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        ElectricalDeviceKind[] lights = [ElectricalDeviceKind.CeilingFixture, ElectricalDeviceKind.Downlight,
            ElectricalDeviceKind.WallSconce, ElectricalDeviceKind.Pendant, ElectricalDeviceKind.CeilingFan];
        ElectricalDeviceKind[] equipment = [
            ElectricalDeviceKind.Outlet, ElectricalDeviceKind.GfciOutlet, ElectricalDeviceKind.UsbOutlet,
            ElectricalDeviceKind.Outlet240, ElectricalDeviceKind.Switch, ElectricalDeviceKind.ThreeWaySwitch,
            ElectricalDeviceKind.Dimmer, ElectricalDeviceKind.Panel, ElectricalDeviceKind.MiniSplit,
            ElectricalDeviceKind.WaterPump, ElectricalDeviceKind.SolarArray, ElectricalDeviceKind.Inverter, ElectricalDeviceKind.SmokeDetector,
            ElectricalDeviceKind.EmergencyLight, ElectricalDeviceKind.ExitSign, ElectricalDeviceKind.FireAlarmPullStation,
            ElectricalDeviceKind.HornStrobe, ElectricalDeviceKind.Sprinkler, ElectricalDeviceKind.FireExtinguisher,
            ElectricalDeviceKind.FireHoseCabinet
        ];
        WriteFixture(folder, "electrical", equipment.Concat(lights).ToArray());
        WriteFixture(folder, "electrical-lights", lights);
    }

    /// <summary>Creates one reference project and its source-application mesh.</summary>
    private static void WriteFixture(string folder, string name, ElectricalDeviceKind[] kinds)
    {
        var document = BuildingDocument.Create("Electrical geometry reference");
        var level = document.Levels[0].Id;
        for (var index = 0; index < kinds.Length; index++)
        for (var rotation = 0; rotation < 2; rotation++)
        {
            var position = new Point2(index * 2, rotation * 3);
            document.ElectricalDevices.Add(new ElectricalDevice(new Guid(index * 2 + rotation + 1, 0, 0, new byte[8]),
                level, kinds[index], $"Device {index}-{rotation}", position, 1.3)
                { Facing = rotation == 0 ? 0 : 0.73, IsOn = rotation == 0,
                    PanelCount = kinds[index] == ElectricalDeviceKind.SolarArray ? rotation == 0 ? 4 : 10 : 0,
                    Tilt = kinds[index] == ElectricalDeviceKind.SolarArray ? rotation == 0 ? 15 : 37 : 0,
                    OutdoorPosition = kinds[index] == ElectricalDeviceKind.MiniSplit && rotation == 1
                        ? new Point2(position.X + 1.2, position.Y + 1.7) : null });
        }
        var path = Path.Combine(folder, $"{name}.gr");
        NativeDocumentStore.SaveProject(path, document);
        var mesh = BuildingMesher.Build(ProjectFile.Load(path), includeGround: false, plants: PlantDetail.Render);
        MeshReference.WriteMesh(Path.Combine(folder, $"{name}.refmesh"), mesh);
        Console.WriteLine($"Wrote {document.ElectricalDevices.Count} devices and {mesh.TriangleCount} reference triangles.");
    }
}
