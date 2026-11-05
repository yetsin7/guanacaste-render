using System.Numerics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>
/// The solids of electrical devices: wall plates with their slots or rocker, the panel box, and luminaires whose diffuser
/// glows (<see cref="SurfaceKind.LightEmitter"/>) when the simulation says they are lit and is plain white
/// (<see cref="SurfaceKind.LightOff"/>) when they are not.
/// </summary>
public static class ElectricalGeometry
{
    /// <summary>Appends one device standing on a level at <paramref name="datum"/>, lit to <paramref name="brightness"/>.</summary>
    public static void AppendTo(SceneMesh mesh, ElectricalDevice device, double datum, double brightness)
    {
        var position = new Vector3((float)device.Position.X, (float)device.Position.Y, (float)(datum + device.Elevation));
        var outward = new Vector3(MathF.Cos((float)device.Facing), MathF.Sin((float)device.Facing), 0);
        var along = new Vector3(-outward.Y, outward.X, 0);
        var glow = brightness > 0 ? SurfaceKind.LightEmitter : SurfaceKind.LightOff;
        switch (device.Kind)
        {
            case ElectricalDeviceKind.Outlet or ElectricalDeviceKind.GfciOutlet or ElectricalDeviceKind.UsbOutlet:
                Box(mesh, position, along, outward, 0.0375f, 0.004f, 0.004f, -0.06f, 0.06f, SurfaceKind.Ceramic);
                foreach (var row in new[] { -0.025f, 0.025f })
                {
                    // Two outlets per plate, each with two slots and the ground hole.
                    foreach (var slot in new[] { -0.007f, 0.007f })
                        Box(mesh, position + along * slot, along, outward, 0.0012f, 0.0015f, 0.009f, row - 0.006f, row + 0.006f, SurfaceKind.OpeningFrame);
                    Box(mesh, position, along, outward, 0.002f, 0.0015f, 0.009f, row - 0.016f, row - 0.012f, SurfaceKind.OpeningFrame);
                }
                if (device.Kind == ElectricalDeviceKind.GfciOutlet)
                    Box(mesh, position, along, outward, 0.012f, 0.002f, 0.0095f, -0.004f, 0.004f, SurfaceKind.Metal);
                break;
            case ElectricalDeviceKind.Outlet240:
                Box(mesh, position, along, outward, 0.06f, 0.005f, 0.005f, -0.06f, 0.06f, SurfaceKind.Ceramic);
                Box(mesh, position, along, outward, 0.025f, 0.0015f, 0.011f, -0.025f, 0.025f, SurfaceKind.OpeningFrame);
                break;
            case ElectricalDeviceKind.Switch or ElectricalDeviceKind.ThreeWaySwitch or ElectricalDeviceKind.Dimmer:
            {
                Box(mesh, position, along, outward, 0.0375f, 0.004f, 0.004f, -0.06f, 0.06f, SurfaceKind.Ceramic);
                // The rocker shows its position: pressed at the top when on.
                var tilt = device.IsOn ? 0.004f : -0.004f;
                Box(mesh, position, along, outward, 0.016f, 0.008f, 0.012f + tilt, 0, 0.03f, SurfaceKind.Ceramic);
                Box(mesh, position, along, outward, 0.016f, 0.008f, 0.012f - tilt, -0.03f, 0, SurfaceKind.Ceramic);
                if (device.Kind == ElectricalDeviceKind.Dimmer)
                    Box(mesh, position, along, outward, 0.004f, 0.012f, 0.014f, -0.045f, -0.035f, SurfaceKind.Metal);
                break;
            }
            case ElectricalDeviceKind.Panel:
                Box(mesh, position, along, outward, 0.21f, 0.05f, 0.05f, -0.30f, 0.30f, SurfaceKind.Metal);
                Box(mesh, position, along, outward, 0.16f, 0.004f, 0.103f, -0.24f, 0.20f, SurfaceKind.OpeningFrame);
                Box(mesh, position + along * 0.15f, along, outward, 0.01f, 0.006f, 0.106f, -0.05f, 0.05f, SurfaceKind.Metal);
                break;
            case ElectricalDeviceKind.CeilingFixture:
                Disc(mesh, position, 0.20f, -0.025f, 0, SurfaceKind.Metal);
                Disc(mesh, position, 0.17f, -0.09f, -0.025f, glow);
                break;
            case ElectricalDeviceKind.Downlight:
                Disc(mesh, position, 0.075f, -0.008f, 0.004f, SurfaceKind.Metal);
                Disc(mesh, position, 0.055f, -0.010f, -0.006f, brightness > 0 ? SurfaceKind.SpotEmitter : SurfaceKind.LightOff);
                break;
            case ElectricalDeviceKind.WallSconce:
                Box(mesh, position, along, outward, 0.05f, 0.012f, 0.012f, -0.08f, 0.08f, SurfaceKind.Metal);
                Box(mesh, position + outward * 0.06f, along, outward, 0.07f, 0.05f, 0.05f, -0.12f, 0.12f, glow);
                break;
            case ElectricalDeviceKind.MiniSplit:
            {
                // Indoor unit: a rounded white casing on the wall, its louvre, and the line set leaving from behind its
                // left end, through the wall and down the outside to the condenser.
                Box(mesh, position, along, outward, 0.40f, 0.095f, 0.10f, -0.14f, 0.14f, SurfaceKind.Ceramic);
                Box(mesh, position, along, outward, 0.39f, 0.08f, 0.11f, 0.14f, 0.15f, SurfaceKind.Ceramic);
                Box(mesh, position, along, outward, 0.36f, 0.012f, 0.20f, -0.13f, -0.09f, SurfaceKind.OpeningFrame);
                Box(mesh, position + along * 0.30f, along, outward, 0.012f, 0.004f, 0.196f, 0.02f, 0.035f, SurfaceKind.LightEmitter);
                if (device.OutdoorPosition is not Point2 outdoorAt) break;
                var outdoor = new Vector3((float)outdoorAt.X, (float)outdoorAt.Y, (float)datum);
                var front = -outward;
                var lineZ = position.Z - 0.05f;
                foreach (var (offset, radius) in new[] { (-0.33f, 0.011f), (-0.29f, 0.015f) })
                {
                    // The condenser stands 45 cm out from the wall, so 41 cm back toward the wall is just outside its face.
                    var start = position + along * offset + outward * 0.03f;
                    var face = outdoor + outward * 0.41f + along * offset;
                    face.Z = lineZ;
                    var low = face with { Z = (float)datum + 0.40f };
                    var into = outdoor + outward * 0.12f + along * offset;
                    into.Z = (float)datum + 0.40f;
                    start.Z = lineZ;
                    PlumbingGeometry.Tube(mesh, start, face, radius, SurfaceKind.OpeningFrame);
                    PlumbingGeometry.Tube(mesh, face, low, radius, SurfaceKind.OpeningFrame);
                    PlumbingGeometry.Tube(mesh, low, into, radius, SurfaceKind.OpeningFrame);
                }
                // Outdoor condenser on its concrete pad: casing, the dark fan grille on its front and a side service cover.
                Box(mesh, outdoor, along, front, 0.46f, 0.22f, 0.02f, 0, 0.08f, SurfaceKind.Structure);
                Box(mesh, outdoor, along, front, 0.40f, 0.15f, 0.02f, 0.10f, 0.66f, SurfaceKind.Ceramic);
                Box(mesh, outdoor - along * 0.08f, along, front, 0.22f, 0.006f, 0.172f, 0.16f, 0.60f, SurfaceKind.OpeningFrame);
                Box(mesh, outdoor + along * 0.30f, along, front, 0.07f, 0.006f, 0.172f, 0.20f, 0.45f, SurfaceKind.Metal);
                foreach (var side in new[] { -0.36f, 0.36f })
                    Box(mesh, outdoor + along * side, along, front, 0.02f, 0.17f, 0.02f, 0.08f, 0.10f, SurfaceKind.Metal);
                break;
            }
            case ElectricalDeviceKind.SolarArray:
                AddArray(mesh, device, datum);
                break;
            case ElectricalDeviceKind.Inverter:
                // A white inverter box on the wall with its status light, and the grey AC disconnect beside it.
                Box(mesh, position, along, outward, 0.22f, 0.09f, 0.09f, -0.30f, 0.30f, SurfaceKind.Ceramic);
                Box(mesh, position + along * 0.10f, along, outward, 0.03f, 0.004f, 0.182f, 0.18f, 0.22f, SurfaceKind.LightEmitter);
                Box(mesh, position - along * 0.38f, along, outward, 0.09f, 0.06f, 0.06f, -0.20f, 0.05f, SurfaceKind.Metal);
                break;
            case ElectricalDeviceKind.SmokeDetector:
                // A white dome on the ceiling with its small red indicator.
                Disc(mesh, position, 0.065f, -0.035f, 0, SurfaceKind.Ceramic);
                Disc(mesh, position, 0.05f, -0.045f, -0.035f, SurfaceKind.Ceramic);
                Disc(mesh, position + new Vector3(0.03f, 0, 0), 0.004f, -0.047f, -0.044f, SurfaceKind.OpeningFrame);
                break;
            case ElectricalDeviceKind.Sprinkler:
                // A pendent head: the red drop from the branch above the ceiling, the brass body and its deflector disc.
                Disc(mesh, position, 0.012f, 0f, 0.25f, SurfaceKind.FireRed);
                Disc(mesh, position, 0.04f, -0.008f, 0f, SurfaceKind.Metal);
                Disc(mesh, position, 0.01f, -0.045f, -0.008f, SurfaceKind.Metal);
                Disc(mesh, position, 0.028f, -0.05f, -0.045f, SurfaceKind.Metal);
                break;
            case ElectricalDeviceKind.EmergencyLight:
                // A white battery box with two adjustable lamp heads on top.
                Box(mesh, position, along, outward, 0.17f, 0.04f, 0.04f, -0.06f, 0.06f, SurfaceKind.Ceramic);
                foreach (var side in new[] { -0.10f, 0.10f })
                    Box(mesh, position + along * side, along, outward, 0.035f, 0.035f, 0.07f, 0.06f, 0.11f, glow);
                break;
            case ElectricalDeviceKind.ExitSign:
                // A green-lit sign panel on its wall bracket, always lit from its battery.
                Box(mesh, position, along, outward, 0.17f, 0.012f, 0.012f, -0.10f, 0.10f, SurfaceKind.Metal);
                Box(mesh, position, along, outward, 0.15f, 0.004f, 0.026f, -0.08f, 0.08f, SurfaceKind.LightEmitter);
                break;
            case ElectricalDeviceKind.FireAlarmPullStation:
                Box(mesh, position, along, outward, 0.065f, 0.03f, 0.03f, -0.085f, 0.085f, SurfaceKind.FireRed);
                Box(mesh, position, along, outward, 0.03f, 0.006f, 0.064f, -0.02f, 0.02f, SurfaceKind.Ceramic);
                break;
            case ElectricalDeviceKind.HornStrobe:
                Box(mesh, position, along, outward, 0.07f, 0.03f, 0.03f, -0.07f, 0.07f, SurfaceKind.FireRed);
                Box(mesh, position, along, outward, 0.03f, 0.012f, 0.07f, 0.01f, 0.05f, SurfaceKind.LightOff);
                break;
            case ElectricalDeviceKind.FireExtinguisher:
            {
                // The red cylinder on its wall hook, with the black valve and hose on top.
                var cylinder = position + outward * 0.11f;
                Disc(mesh, cylinder, 0.08f, -0.55f, 0f, SurfaceKind.FireRed);
                Disc(mesh, cylinder, 0.025f, 0f, 0.09f, SurfaceKind.OpeningFrame);
                Box(mesh, position, along, outward, 0.03f, 0.015f, 0.015f, -0.10f, -0.02f, SurfaceKind.Metal);
                break;
            }
            case ElectricalDeviceKind.FireHoseCabinet:
                // A recessed red cabinet with its glazed door and the hose reel visible behind it.
                Box(mesh, position, along, outward, 0.40f, 0.10f, 0.08f, 0f, 0.80f, SurfaceKind.FireRed);
                Box(mesh, position, along, outward, 0.34f, 0.004f, 0.182f, 0.06f, 0.74f, SurfaceKind.Glass);
                Box(mesh, position, along, outward, 0.22f, 0.06f, 0.09f, 0.18f, 0.62f, SurfaceKind.FireRed);
                break;
            case ElectricalDeviceKind.WaterPump:
            {
                // Concrete pad, the blue pressure tank standing on it, and the pump with its motor lying beside it.
                Box(mesh, position, along, outward, 0.45f, 0.30f, 0, 0, 0.08f, SurfaceKind.Structure);
                Disc(mesh, position + along * 0.18f, 0.15f, 0.08f, 0.62f, SurfaceKind.PipeCold);
                Disc(mesh, position + along * 0.18f, 0.03f, 0.62f, 0.66f, SurfaceKind.Metal);
                var motor = position - along * 0.18f;
                motor.Z += 0.20f;
                PlumbingGeometry.Tube(mesh, motor - outward * 0.17f, motor + outward * 0.17f, 0.09f, SurfaceKind.Metal);
                PlumbingGeometry.Tube(mesh, motor + outward * 0.17f, motor + outward * 0.26f, 0.07f, SurfaceKind.PipeCold);
                Box(mesh, position - along * 0.18f, along, outward, 0.09f, 0.12f, 0, 0.08f, 0.12f, SurfaceKind.Metal);
                break;
            }
            case ElectricalDeviceKind.CeilingFan:
            {
                // Canopy, a 30 cm downrod, the motor housing, five blades on their irons, and the light kit under it.
                Disc(mesh, position, 0.07f, -0.05f, 0f, SurfaceKind.Metal);
                Disc(mesh, position, 0.012f, -0.32f, -0.05f, SurfaceKind.Metal);
                Disc(mesh, position, 0.10f, -0.44f, -0.32f, SurfaceKind.Metal);
                for (var blade = 0; blade < 5; blade++)
                {
                    var angle = blade * MathF.Tau / 5 + 0.3f;
                    var radial = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
                    var tangent = new Vector3(-radial.Y, radial.X, 0);
                    Box(mesh, position, tangent, radial, 0.012f, 0.06f, 0.14f, -0.42f, -0.41f, SurfaceKind.Metal);
                    Box(mesh, position, tangent, radial, 0.065f, 0.27f, 0.42f, -0.425f, -0.415f, SurfaceKind.DoorLeaf);
                }
                Disc(mesh, position, 0.09f, -0.50f, -0.44f, glow);
                break;
            }
            case ElectricalDeviceKind.Pendant:
                Disc(mesh, position, 0.05f, -0.02f, 0, SurfaceKind.Metal);
                Disc(mesh, position, 0.004f, -0.70f, -0.02f, SurfaceKind.OpeningFrame);
                Disc(mesh, position, 0.17f, -0.95f, -0.70f, SurfaceKind.OpeningFrame);
                Disc(mesh, position, 0.15f, -0.96f, -0.93f, glow);
                break;
        }
    }

    /// <summary>
    /// A photovoltaic array: portrait modules in rows on aluminium rails, tilted up-slope away from the direction they face,
    /// each a dark cell face in a slim metal frame.
    /// </summary>
    private static void AddArray(SceneMesh mesh, ElectricalDevice array, double datum)
    {
        var facing = new Vector3(MathF.Cos((float)array.Facing), MathF.Sin((float)array.Facing), 0);
        var across = new Vector3(-facing.Y, facing.X, 0);
        var tilt = (float)(array.Tilt * Math.PI / 180);
        var upSlope = Vector3.Normalize(-facing * MathF.Cos(tilt) + Vector3.UnitZ * MathF.Sin(tilt));
        var normal = Vector3.Normalize(facing * MathF.Sin(tilt) + Vector3.UnitZ * MathF.Cos(tilt));
        var center = new Vector3((float)array.Position.X, (float)array.Position.Y, (float)(datum + array.Elevation));
        var (rows, columns) = SolarGeometry.Layout(array.PanelCount);
        const float gap = 0.02f;
        var length = (float)ElectricalStandards.PanelLength;
        var width = (float)ElectricalStandards.PanelWidth;
        var placed = 0;
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns && placed < array.PanelCount; column++, placed++)
            {
                var offsetAcross = (column - (columns - 1) / 2f) * (width + gap);
                var offsetUp = (row - (rows - 1) / 2f) * (length + gap);
                var middle = center + across * offsetAcross + upSlope * offsetUp;
                Oriented(mesh, middle + normal * 0.02f, across, upSlope, normal, width / 2 - 0.02f, length / 2 - 0.02f, 0.004f, SurfaceKind.SolarCell);
                Oriented(mesh, middle, across, upSlope, normal, width / 2, length / 2, 0.018f, SurfaceKind.Metal);
            }
        // Two rails under each row of modules, the full width of the array.
        for (var row = 0; row < rows; row++)
            foreach (var at in new[] { -0.25f, 0.25f })
            {
                var middle = center + upSlope * ((row - (rows - 1) / 2f) * (length + gap) + at * length) - normal * 0.04f;
                Oriented(mesh, middle, across, upSlope, normal, columns * (width + gap) / 2, 0.02f, 0.02f, SurfaceKind.Metal);
            }
    }

    /// <summary>A box with arbitrary axes: half-extents along each unit axis around its centre.</summary>
    private static void Oriented(SceneMesh mesh, Vector3 center, Vector3 u, Vector3 v, Vector3 n, float hu, float hv, float hn, SurfaceKind kind)
    {
        Vector3 P(int su, int sv, int sn) => center + u * (su * hu) + v * (sv * hv) + n * (sn * hn);
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => mesh.AddQuad(a, b, c, d, kind);
        Face(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        Face(P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), P(-1, -1, -1));
        Face(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        Face(P(1, 1, -1), P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1));
        Face(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1));
        Face(P(-1, 1, -1), P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1));
    }

    /// <summary>A box centred on <paramref name="origin"/> along the wall, offset out from it, between two heights.</summary>
    private static void Box(SceneMesh mesh, Vector3 origin, Vector3 along, Vector3 outward, float halfAlong, float halfOut,
        float offsetOut, float bottom, float top, SurfaceKind kind)
    {
        var center = origin + outward * offsetOut;
        Vector3 P(float a, float o, float z) => center + along * a + outward * o + new Vector3(0, 0, z);
        var corners = new[] { P(-halfAlong, -halfOut, 0), P(halfAlong, -halfOut, 0), P(halfAlong, halfOut, 0), P(-halfAlong, halfOut, 0) };
        Prism(mesh, corners, bottom, top, kind);
    }

    /// <summary>A twelve-sided disc or cylinder around a vertical axis.</summary>
    private static void Disc(SceneMesh mesh, Vector3 center, float radius, float bottom, float top, SurfaceKind kind)
    {
        const int sides = 16;
        var ring = Enumerable.Range(0, sides).Select(index => center + new Vector3(
            radius * MathF.Cos(index * MathF.Tau / sides), radius * MathF.Sin(index * MathF.Tau / sides), 0)).ToArray();
        Prism(mesh, ring, bottom, top, kind);
    }

    /// <summary>A vertical prism over a counter-clockwise (or clockwise) outline, between two heights relative to its points.</summary>
    private static void Prism(SceneMesh mesh, Vector3[] outline, float bottom, float top, SurfaceKind kind)
    {
        var area = 0f;
        for (var index = 0; index < outline.Length; index++)
        {
            var a = outline[index];
            var b = outline[(index + 1) % outline.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        if (area < 0) Array.Reverse(outline);
        var up = new Vector3(0, 0, top);
        var down = new Vector3(0, 0, bottom);
        for (var index = 1; index + 1 < outline.Length; index++)
        {
            mesh.AddTriangle(outline[0] + up, outline[index] + up, outline[index + 1] + up, kind);
            mesh.AddTriangle(outline[index + 1] + down, outline[index] + down, outline[0] + down, kind);
        }
        for (var index = 0; index < outline.Length; index++)
        {
            var a = outline[index];
            var b = outline[(index + 1) % outline.Length];
            mesh.AddQuad(a + down, b + down, b + up, a + up, kind);
        }
    }
}
