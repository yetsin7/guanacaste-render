using System.Numerics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>
/// The solids of the plumbing: every routed pipe as a round tube of its outside diameter, coloured by system as in
/// Revit's system graphics (blue cold, red hot, PVC drain), an angle stop where each supply riser meets its fixture,
/// the meter box and the outlet's cleanout cap.
/// </summary>
public static class PlumbingGeometry
{
    public static void AppendTo(SceneMesh mesh, BuildingDocument document, PlumbingAnalysis analysis)
    {
        foreach (var report in analysis.Systems)
        {
            var kind = Kind(report.System.Kind);
            foreach (var run in report.Runs)
                Tube(mesh, new Vector3((float)run.From.X, (float)run.From.Y, (float)run.FromZ),
                    new Vector3((float)run.To.X, (float)run.To.Y, (float)run.ToZ), (float)(run.OutsideMillimeters / 2000), kind);
            foreach (var vent in report.Vents)
            {
                var foot = new Vector3((float)vent.Position.X, (float)vent.Position.Y, (float)vent.Bottom);
                var top = foot with { Z = (float)vent.Top };
                var radius = (float)(vent.OutsideMillimeters / 2000);
                Tube(mesh, foot, top, radius, SurfaceKind.PipeDrain);
                // The vent cap, a short wider sleeve that keeps rain out.
                Tube(mesh, top - new Vector3(0, 0, 0.06f), top + new Vector3(0, 0, 0.04f), radius + 0.015f, SurfaceKind.PipeDrain);
            }
            if (report.System.Kind == PlumbingSystemKind.Sanitary) continue;
            // An angle stop (llave de paso) at the top of each riser.
            foreach (var run in report.Runs.Where(run => run.From.DistanceTo(run.To) < 1e-9 && run.ToZ > run.FromZ))
            {
                var top = new Vector3((float)run.To.X, (float)run.To.Y, (float)run.ToZ);
                Tube(mesh, top - new Vector3(0, 0, 0.02f), top + new Vector3(0, 0, 0.03f), 0.016f, SurfaceKind.Metal);
            }
        }
        var elevations = document.Levels.ToDictionary(level => level.Id, level => level.Elevation);
        foreach (var septic in analysis.Septic)
            if (document.PlumbingPoints.FirstOrDefault(point => point.Id == septic.Id) is { } tank &&
                elevations.TryGetValue(tank.LevelId, out var tankLevel))
                AddSeptic(mesh, septic, tankLevel);
        foreach (var point in document.PlumbingPoints)
        {
            if (!elevations.TryGetValue(point.LevelId, out var level)) continue;
            if (point.Kind is PlumbingPointKind.SepticTank or PlumbingPointKind.VentStack) continue;
            if (point.Kind is PlumbingPointKind.Cistern or PlumbingPointKind.ElevatedTank)
            {
                var design = analysis.Storage.FirstOrDefault(storage => storage.Id == point.Id);
                AddTank(mesh, point, level, design?.CapacityLiters ?? 1100);
                continue;
            }
            var at = new Vector3((float)point.Position.X, (float)point.Position.Y, (float)level);
            if (point.Kind == PlumbingPointKind.WaterMeter)
            {
                // A meter box flush with the ground, its lid slightly raised.
                Tube(mesh, at - new Vector3(0, 0, 0.25f), at + new Vector3(0, 0, 0.015f), 0.17f, SurfaceKind.Structure);
                Tube(mesh, at + new Vector3(0, 0, 0.015f), at + new Vector3(0, 0, 0.025f), 0.15f, SurfaceKind.Metal);
            }
            else
            {
                // A cleanout cap at grade: grey for the sewer, green for the storm outlet.
                Tube(mesh, at - new Vector3(0, 0, 0.02f), at + new Vector3(0, 0, 0.012f), 0.07f,
                    point.Kind == PlumbingPointKind.StormOutlet ? SurfaceKind.PipeStorm : SurfaceKind.PipeDrain);
            }
        }
    }

    private static SurfaceKind Kind(PlumbingSystemKind kind) => kind switch
    {
        PlumbingSystemKind.ColdWater => SurfaceKind.PipeCold,
        PlumbingSystemKind.HotWater => SurfaceKind.PipeHot,
        PlumbingSystemKind.StormWater => SurfaceKind.PipeStorm,
        _ => SurfaceKind.PipeDrain
    };

    /// <summary>
    /// A septic tank buried beside the house: concrete walls, floor and lid, a baffle wall making two chambers, two
    /// access covers at grade; then the outlet pipe to a distribution box and the gravel trenches of the drain field,
    /// each with its perforated pipe.
    /// </summary>
    private static void AddSeptic(SceneMesh mesh, SepticReport septic, double level)
    {
        var d = new Vector3((float)septic.FieldDirection.X, (float)septic.FieldDirection.Y, 0);
        var n = new Vector3(-d.Y, d.X, 0);
        var center = new Vector3((float)septic.Position.X, (float)septic.Position.Y, 0);
        var halfL = (float)septic.Length / 2 + 0.10f;
        var halfW = (float)septic.Width / 2 + 0.10f;
        var top = (float)level - 0.25f;
        var bottom = top - (float)septic.LiquidDepth - 0.40f;
        const float wall = 0.10f;
        Block(mesh, center, d, n, -halfL, halfL, -halfW, halfW, bottom, bottom + wall, SurfaceKind.Structure);
        Block(mesh, center, d, n, -halfL, halfL, -halfW, halfW, top - wall, top, SurfaceKind.Structure);
        Block(mesh, center, d, n, -halfL, -halfL + wall, -halfW, halfW, bottom, top, SurfaceKind.Structure);
        Block(mesh, center, d, n, halfL - wall, halfL, -halfW, halfW, bottom, top, SurfaceKind.Structure);
        Block(mesh, center, d, n, -halfL, halfL, -halfW, -halfW + wall, bottom, top, SurfaceKind.Structure);
        Block(mesh, center, d, n, -halfL, halfL, halfW - wall, halfW, bottom, top, SurfaceKind.Structure);
        // The baffle at two thirds of the length keeps scum and sludge in the first chamber.
        var baffle = -halfL + 2 * halfL * 2 / 3;
        Block(mesh, center, d, n, baffle - 0.04f, baffle + 0.04f, -halfW + wall, halfW - wall, bottom + 0.3f, top - 0.15f, SurfaceKind.Structure);
        foreach (var at in new[] { -halfL * 0.55f, halfL * 0.65f })
            Tube(mesh, center + d * at + new Vector3(0, 0, top), center + d * at + new Vector3(0, 0, (float)level + 0.03f), 0.30f, SurfaceKind.Metal);
        // Outlet to the distribution box, then the trenches running on away from the house.
        var pipeZ = top - 0.30f;
        var box = center + d * (halfL + 1.5f);
        Tube(mesh, center + d * halfL + new Vector3(0, 0, pipeZ), box + new Vector3(0, 0, pipeZ - 0.03f), 0.055f, SurfaceKind.PipeDrain);
        Block(mesh, box, d, n, -0.3f, 0.3f, -0.3f, 0.3f, pipeZ - 0.35f, (float)level - 0.05f, SurfaceKind.Structure);
        var trenchLength = (float)(septic.FieldLength / septic.Trenches);
        for (var trench = 0; trench < septic.Trenches; trench++)
        {
            var offset = (trench - (septic.Trenches - 1) / 2f) * 2f;
            var start = box + d * 1.0f + n * offset;
            var gravelTop = pipeZ - 0.02f;
            Block(mesh, start, d, n, 0, trenchLength, -(float)SiteDrainage.TrenchWidth / 2, (float)SiteDrainage.TrenchWidth / 2,
                gravelTop - 0.35f, gravelTop, SurfaceKind.Earth);
            Tube(mesh, start + new Vector3(0, 0, gravelTop - 0.10f), start + d * trenchLength + new Vector3(0, 0, gravelTop - 0.10f - 0.005f * trenchLength),
                0.05f, SurfaceKind.PipeDrain);
            if (Math.Abs(offset) > 0.01f)
                Tube(mesh, box + new Vector3(0, 0, gravelTop - 0.10f), start + new Vector3(0, 0, gravelTop - 0.10f), 0.05f, SurfaceKind.PipeDrain);
        }
    }

    /// <summary>
    /// A black polyethylene tank: on a concrete pad for a cistern, or on a four-legged steel tower with braces and a
    /// platform for an elevated tank, with its lid and the outlet down to the ground.
    /// </summary>
    private static void AddTank(SceneMesh mesh, PlumbingPoint point, double level, double liters)
    {
        var (diameter, height) = WaterStorage.TankShape(liters);
        var radius = (float)diameter / 2;
        var at = new Vector3((float)point.Position.X, (float)point.Position.Y, (float)level);
        var floor = at.Z;
        if (point.Kind == PlumbingPointKind.ElevatedTank)
        {
            floor = at.Z + (float)point.TankHeight;
            var half = radius * 0.8f + 0.1f;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                Tube(mesh, at + new Vector3(sx * half, sy * half, 0), at + new Vector3(sx * half, sy * half, floor - at.Z), 0.05f, SurfaceKind.Metal, 4);
            for (var z = 1.5f; z < floor - at.Z - 0.3f; z += 1.8f)
                foreach (var ((ax, ay), (bx, by)) in new[] { ((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1)) })
                    Tube(mesh, at + new Vector3(ax * half, ay * half, z), at + new Vector3(bx * half, by * half, z + 0.9f), 0.025f, SurfaceKind.Metal, 4);
            Block(mesh, at, Vector3.UnitX, Vector3.UnitY, -half - 0.1f, half + 0.1f, -half - 0.1f, half + 0.1f, floor - at.Z - 0.08f, floor - at.Z,
                SurfaceKind.Metal);
            // The outlet pipe down the tower to the ground.
            Tube(mesh, at + new Vector3(0, 0, floor - at.Z), at, 0.025f, SurfaceKind.PipeCold);
        }
        else
            Block(mesh, at, Vector3.UnitX, Vector3.UnitY, -radius - 0.15f, radius + 0.15f, -radius - 0.15f, radius + 0.15f, 0, 0.10f, SurfaceKind.Structure);
        var bottom = point.Kind == PlumbingPointKind.ElevatedTank ? floor : floor + 0.10f;
        Tube(mesh, at with { Z = bottom }, at with { Z = bottom + (float)height }, radius, SurfaceKind.OpeningFrame, 20);
        Tube(mesh, at with { Z = bottom + (float)height }, at with { Z = bottom + (float)height + 0.08f }, radius * 0.75f, SurfaceKind.OpeningFrame, 20);
        Tube(mesh, at with { Z = bottom + (float)height + 0.08f }, at with { Z = bottom + (float)height + 0.14f }, 0.25f, SurfaceKind.OpeningFrame, 12);
    }

    /// <summary>A box in a local frame: along <paramref name="d"/> from a0 to a1, across <paramref name="n"/> from b0 to b1.</summary>
    private static void Block(SceneMesh mesh, Vector3 origin, Vector3 d, Vector3 n, float a0, float a1, float b0, float b1,
        float bottom, float top, SurfaceKind kind)
    {
        Vector3 P(float a, float b, float z) => new Vector3(origin.X, origin.Y, 0) + d * a + n * b + new Vector3(0, 0, z);
        var corners = new[] { (a0, b0), (a1, b0), (a1, b1), (a0, b1) };
        for (var index = 0; index < 4; index++)
        {
            var (aa, ab) = corners[index];
            var (ba, bb) = corners[(index + 1) % 4];
            mesh.AddQuad(P(aa, ab, bottom), P(ba, bb, bottom), P(ba, bb, top), P(aa, ab, top), kind);
        }
        mesh.AddQuad(P(a0, b0, top), P(a1, b0, top), P(a1, b1, top), P(a0, b1, top), kind);
        mesh.AddQuad(P(a0, b1, bottom), P(a1, b1, bottom), P(a1, b0, bottom), P(a0, b0, bottom), kind);
    }

    /// <summary>An eight-sided tube with closed ends from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static void Tube(SceneMesh mesh, Vector3 a, Vector3 b, float radius, SurfaceKind kind, int sides = 8)
    {
        var axis = b - a;
        var length = axis.Length();
        if (length < 1e-5f || radius <= 0) return;
        var direction = axis / length;
        var helper = MathF.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(helper, direction));
        // (u, v, direction) is right-handed, so the rings below run counter-clockwise seen from b and the quads face out.
        var v = Vector3.Cross(direction, u);
        var ringA = new Vector3[sides];
        var ringB = new Vector3[sides];
        for (var index = 0; index < sides; index++)
        {
            var angle = index * MathF.Tau / sides;
            var offset = (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            ringA[index] = a + offset;
            ringB[index] = b + offset;
        }
        for (var index = 0; index < sides; index++)
        {
            var next = (index + 1) % sides;
            mesh.AddQuad(ringA[index], ringA[next], ringB[next], ringB[index], kind);
        }
        for (var index = 1; index + 1 < sides; index++)
        {
            mesh.AddTriangle(ringB[0], ringB[index], ringB[index + 1], kind);
            mesh.AddTriangle(ringA[0], ringA[index + 1], ringA[index], kind);
        }
    }
}
