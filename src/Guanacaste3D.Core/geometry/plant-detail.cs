using System.Collections.Concurrent;
using System.Numerics;

namespace Guanacaste3D.Core.Geometry;

/// <summary>How much of a plant to build: lumpy canopies for modelling views, branches and single leaves for renders.</summary>
public enum PlantDetail
{
    Model,
    Render
}

/// <summary>
/// Render-quality vegetation. A tree is grown, not sculpted: a trunk with a root flare splits into scaffold limbs that
/// branch recursively (each child thinner by Leonardo's rule, so the cross-section is conserved), bending toward light
/// and kept inside the species' crown envelope; the twigs carry clusters of individual leaves with a folded midrib.
/// Palms get a ringed trunk and fronds of single pinnae, conifers whorls of drooping branches with needle sprays. Every
/// leaf is real geometry, so sunlight falls through the gaps of the crown in dapples. The result is fitted to the
/// plant's height and spread like the modelling geometry, and cached, because a tree has tens of thousands of leaves.
/// </summary>
public static class PlantDetailGeometry
{
    private static readonly ConcurrentDictionary<(StructuralElement Plant, double Base), (Vector3[] Positions, Vector3[] Normals, SurfaceKind[] Kinds)> Cache = new();

    /// <summary>Appends the render-quality plant standing at <paramref name="baseElevation"/>.</summary>
    public static void AppendTo(SceneMesh mesh, StructuralElement plant, double baseElevation)
    {
        var key = (plant, baseElevation);
        if (!Cache.TryGetValue(key, out var built))
        {
            var local = new SceneMesh();
            Grow(local, plant, baseElevation);
            built = (local.Positions.ToArray(), local.Normals?.ToArray() ?? new Vector3[local.Positions.Count], local.TriangleKinds.ToArray());
            if (Cache.Count > 64) Cache.Clear();
            Cache[key] = built;
        }
        for (var triangle = 0; triangle < built.Kinds.Length; triangle++)
        {
            var i = triangle * 3;
            if (built.Normals[i] == Vector3.Zero)
                mesh.AddTriangle(built.Positions[i], built.Positions[i + 1], built.Positions[i + 2], built.Kinds[triangle]);
            else
                mesh.AddSmoothTriangle(built.Positions[i], built.Positions[i + 1], built.Positions[i + 2],
                    built.Normals[i], built.Normals[i + 1], built.Normals[i + 2], built.Kinds[triangle]);
        }
    }

    private static void Grow(SceneMesh mesh, StructuralElement plant, double baseElevation)
    {
        var random = new Random(PlantGeometry.Seed(plant.Id));
        var root = new Vector3((float)plant.Start.X, (float)plant.Start.Y, (float)(baseElevation + plant.Offset));
        var height = (float)Math.Max(0.2, plant.Height);
        var spread = (float)Math.Max(0.2, plant.Width);
        var tree = new Tree(mesh, random);
        switch (plant.Style)
        {
            case ElementStyle.Palm: GrowPalm(tree, root, height, spread); break;
            case ElementStyle.Conifer: GrowConifer(tree, root, height, spread); break;
            case ElementStyle.Shrub: GrowShrub(tree, root, height, spread); break;
            case ElementStyle.Guanacaste: GrowGuanacaste(tree, root, height, spread); break;
            default: GrowBroadleaf(tree, root, height, spread); break;
        }
        PlantGeometry.FitToBox(mesh, 0, root, height, spread);
    }

    // ------------------------------------------------------------------ species

    private static void GrowBroadleaf(Tree tree, Vector3 root, float height, float spread)
    {
        var (trunkTop, canopyBottom, trunkRadius) = PlantGeometry.Proportions(ElementStyle.Default, height, spread);
        var crownHeight = height - (float)canopyBottom;
        tree.Envelope = new Envelope(root + new Vector3(0, 0, (float)canopyBottom + crownHeight * 0.5f),
            new Vector3(spread / 2, spread / 2, crownHeight / 2));
        tree.LeafSize = 0.11f;
        tree.LeavesPerTwig = 46;
        tree.MaxDepth = 6;
        var top = tree.Trunk(root, (float)trunkTop, (float)trunkRadius, lean: 0.05f);
        var limbs = 4 + tree.Random.Next(2);
        for (var index = 0; index < limbs; index++)
        {
            var azimuth = (index + PlantGeometry.Jitter(tree.Random, 0.3f)) * MathF.Tau / limbs;
            var tilt = 0.55f + 0.35f * (float)tree.Random.NextDouble();
            var direction = Vector3.Normalize(new Vector3(MathF.Cos(azimuth) * tilt, MathF.Sin(azimuth) * tilt, 1));
            tree.Branch(top, direction, crownHeight * 0.42f, (float)trunkRadius * 0.62f, 1);
        }
        // A leader carries the crown's top.
        tree.Branch(top, Vector3.UnitZ, crownHeight * 0.45f, (float)trunkRadius * 0.55f, 1);
        tree.FillCrown(clusters: 340, shell: 0.55f);
    }

    /// <summary>
    /// Enterolobium cyclocarpum: a short massive trunk forking low into heavy limbs that run out almost level, under a
    /// very wide, flat crown of fine bipinnate foliage (many small leaflets).
    /// </summary>
    private static void GrowGuanacaste(Tree tree, Vector3 root, float height, float spread)
    {
        var (trunkTop, _, trunkRadius) = PlantGeometry.Proportions(ElementStyle.Guanacaste, height, spread);
        var crownBase = height * 0.55f;
        var crownThickness = height - crownBase;
        tree.Envelope = new Envelope(root + new Vector3(0, 0, crownBase + crownThickness * 0.5f),
            new Vector3(spread / 2, spread / 2, crownThickness * 0.62f));
        // Its bipinnate leaves (15–35 cm, hundreds of leaflets) read from any distance as small feathery blades.
        tree.LeafSize = 0.16f;
        tree.LeafWidth = 0.55f;
        tree.LeavesPerTwig = 44;
        tree.MaxDepth = 6;
        tree.UpwardBias = 0.05f;
        var fork = tree.Trunk(root, (float)trunkTop * 0.6f, (float)trunkRadius * 1.1f, lean: 0.02f);
        var limbs = 5 + tree.Random.Next(2);
        for (var index = 0; index < limbs; index++)
        {
            var azimuth = (index + PlantGeometry.Jitter(tree.Random, 0.25f)) * MathF.Tau / limbs;
            var direction = Vector3.Normalize(new Vector3(MathF.Cos(azimuth), MathF.Sin(azimuth), 0.75f + 0.2f * (float)tree.Random.NextDouble()));
            tree.Branch(fork, direction, spread * 0.3f, (float)trunkRadius * 0.55f, 1);
        }
        tree.FillCrown(clusters: 1500, shell: 0.5f, upperOnly: true);
    }

    private static void GrowShrub(Tree tree, Vector3 root, float height, float spread)
    {
        tree.Envelope = new Envelope(root + new Vector3(0, 0, height * 0.5f), new Vector3(spread / 2, spread / 2, height / 2));
        tree.LeafSize = 0.06f;
        tree.LeavesPerTwig = 30;
        tree.MaxDepth = 4;
        var stems = 6 + tree.Random.Next(3);
        for (var index = 0; index < stems; index++)
        {
            var azimuth = index * MathF.Tau / stems + PlantGeometry.Jitter(tree.Random, 0.4f);
            var direction = Vector3.Normalize(new Vector3(MathF.Cos(azimuth) * 0.5f, MathF.Sin(azimuth) * 0.5f, 1));
            tree.Branch(root + new Vector3(PlantGeometry.Jitter(tree.Random, 0.08f), PlantGeometry.Jitter(tree.Random, 0.08f), 0),
                direction, height * 0.5f, 0.018f, 1);
        }
        tree.FillCrown(clusters: (int)Math.Clamp(spread * spread * 40, 60, 400), shell: 0.35f);
    }

    private static void GrowConifer(Tree tree, Vector3 root, float height, float spread)
    {
        var (_, canopyBottom, trunkRadius) = PlantGeometry.Proportions(ElementStyle.Conifer, height, spread);
        tree.Tube(root, root + new Vector3(0, 0, height), (float)trunkRadius * 1.3f, (float)trunkRadius * 0.08f, SurfaceKind.Bark, 10);
        tree.LeafSize = 0.075f;
        tree.LeafWidth = 0.45f;
        tree.LeavesPerTwig = 22;
        var tiers = (int)((height - (float)canopyBottom) / 0.32f);
        for (var tier = 0; tier < tiers; tier++)
        {
            var t = (float)tier / tiers;
            var z = (float)canopyBottom + (height - (float)canopyBottom) * t * 0.97f;
            var reach = spread / 2 * (1 - t) * (0.85f + 0.25f * (float)tree.Random.NextDouble());
            var branches = 5 + tree.Random.Next(3);
            var twist = (float)tree.Random.NextDouble() * MathF.Tau;
            for (var index = 0; index < branches; index++)
            {
                var azimuth = twist + index * MathF.Tau / branches;
                var outward = new Vector3(MathF.Cos(azimuth), MathF.Sin(azimuth), 0);
                var start = root + new Vector3(0, 0, z);
                // Drooping branch: out and slightly down, with sprays of needles on side shoots.
                var end = start + outward * reach + new Vector3(0, 0, -reach * 0.25f);
                tree.Tube(start, end, MathF.Max(0.006f, reach * 0.025f), 0.004f, SurfaceKind.Bark, 4);
                var shoots = Math.Max(2, (int)(reach / 0.12f));
                for (var shoot = 1; shoot <= shoots; shoot++)
                {
                    var at = Vector3.Lerp(start, end, (float)shoot / (shoots + 1));
                    var side = Vector3.Normalize(Vector3.Cross(outward, Vector3.UnitZ)) * (shoot % 2 == 0 ? 1 : -1);
                    var shootEnd = at + (side * 0.6f + outward * 0.6f) * (reach * 0.22f * (1 - (float)shoot / (shoots + 2))) +
                                   new Vector3(0, 0, -0.03f);
                    tree.NeedleSpray(at, shootEnd);
                }
                tree.NeedleSpray(Vector3.Lerp(start, end, 0.85f), end);
            }
        }
        tree.NeedleSpray(root + new Vector3(0, 0, height * 0.94f), root + new Vector3(0, 0, height));
    }

    private static void GrowPalm(Tree tree, Vector3 root, float height, float spread)
    {
        var (trunkTop, _, trunkRadius) = PlantGeometry.Proportions(ElementStyle.Palm, height, spread);
        var leanAngle = (float)tree.Random.NextDouble() * MathF.Tau;
        var lean = new Vector3(MathF.Cos(leanAngle), MathF.Sin(leanAngle), 0) * (height * 0.10f);
        const int segments = 18;
        var previous = root;
        for (var index = 1; index <= segments; index++)
        {
            var t = (float)index / segments;
            var point = root + lean * (t * t) + new Vector3(0, 0, (float)trunkTop * t);
            // Leaf scars ring the trunk: each segment swells at its base.
            tree.Tube(previous, point, (float)trunkRadius * (1.35f - 0.35f * t + 0.06f), (float)trunkRadius * (1.35f - 0.35f * t), SurfaceKind.Bark, 12);
            previous = point;
        }
        var crown = previous;
        // Coconuts hang under the crown.
        for (var index = 0; index < 7; index++)
        {
            var azimuth = index * MathF.Tau / 7 + PlantGeometry.Jitter(tree.Random, 0.3f);
            tree.Sphere(crown + new Vector3(MathF.Cos(azimuth) * 0.22f, MathF.Sin(azimuth) * 0.22f, -0.25f), 0.12f, SurfaceKind.DoorLeaf);
        }
        var fronds = 15 + tree.Random.Next(4);
        for (var index = 0; index < fronds; index++)
        {
            var azimuth = (index + PlantGeometry.Jitter(tree.Random, 0.35f)) * MathF.Tau / fronds;
            var rise = 0.9f - 1.4f * (float)tree.Random.NextDouble();
            tree.Frond(crown, new Vector3(MathF.Cos(azimuth), MathF.Sin(azimuth), 0), rise, spread * (0.5f + 0.08f * (float)tree.Random.NextDouble()));
        }
    }

    // ------------------------------------------------------------------ growth engine

    private readonly record struct Envelope(Vector3 Center, Vector3 Radii)
    {
        /// <summary>Normalized distance: below 1 inside the crown.</summary>
        public float Measure(Vector3 point)
        {
            var d = (point - Center) / Radii;
            return d.Length();
        }

        public Vector3 Outward(Vector3 point)
        {
            var d = point - Center;
            return d.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(d / (Radii * Radii));
        }
    }

    private sealed class Tree(SceneMesh mesh, Random random)
    {
        private readonly List<(Vector3 Position, float Radius)> _tips = [];
        public Random Random { get; } = random;
        /// <summary>Leaf width relative to its length: 1 for broad leaves, about 0.3 for needle sprays.</summary>
        public float LeafWidth { get; set; } = 1;
        public Envelope Envelope { get; set; } = new(Vector3.Zero, Vector3.One);
        public float LeafSize { get; set; } = 0.1f;
        public int LeavesPerTwig { get; set; } = 40;
        public int MaxDepth { get; set; } = 5;
        public float UpwardBias { get; set; } = 0.18f;

        /// <summary>A trunk with a flared base and a slight lean; returns its top.</summary>
        public Vector3 Trunk(Vector3 root, float length, float radius, float lean)
        {
            var leanDirection = new Vector3(PlantGeometry.Jitter(Random, 1), PlantGeometry.Jitter(Random, 1), 0) * lean;
            var flare = root + new Vector3(0, 0, MathF.Min(0.45f, length * 0.15f));
            Tube(root, flare, radius * 1.55f, radius * 1.05f, SurfaceKind.Bark, 14);
            var top = root + new Vector3(leanDirection.X * length, leanDirection.Y * length, length);
            var middle = Vector3.Lerp(flare, top, 0.5f) + new Vector3(PlantGeometry.Jitter(Random, 0.03f), PlantGeometry.Jitter(Random, 0.03f), 0) * length;
            Tube(flare, middle, radius * 1.05f, radius * 0.92f, SurfaceKind.Bark, 14);
            Tube(middle, top, radius * 0.92f, radius * 0.8f, SurfaceKind.Bark, 12);
            return top;
        }

        /// <summary>One branch and, recursively, its children; twigs end in leaf clusters.</summary>
        public void Branch(Vector3 start, Vector3 direction, float length, float radius, int depth)
        {
            // Keep the branch inside the crown: turn it back toward the middle rather than letting it poke out.
            var end = start + direction * length;
            for (var attempt = 0; attempt < 4 && Envelope.Measure(end) > 1.0f; attempt++)
            {
                direction = Vector3.Normalize(direction + Vector3.Normalize(Envelope.Center - end) * 0.6f);
                length *= 0.9f;
                end = start + direction * length;
            }
            // A gentle bend toward the light along the branch.
            var bend = Vector3.UnitZ * (length * 0.12f * UpwardBias * 4) +
                       new Vector3(PlantGeometry.Jitter(Random, 1), PlantGeometry.Jitter(Random, 1), 0) * (length * 0.08f);
            var middle = Vector3.Lerp(start, end, 0.5f) + bend * 0.5f;
            end += bend;
            var tipRadius = radius * 0.72f;
            var sides = radius > 0.06f ? 9 : radius > 0.018f ? 6 : 4;
            Tube(start, middle, radius, (radius + tipRadius) / 2, SurfaceKind.Bark, sides);
            Tube(middle, end, (radius + tipRadius) / 2, tipRadius, SurfaceKind.Bark, sides);

            if (depth >= MaxDepth || length < 0.08f || tipRadius < 0.003f)
            {
                _tips.Add((end, tipRadius));
                LeafCluster(end, Vector3.Normalize(end - start), length);
                return;
            }
            if (depth >= 3) _tips.Add((end, tipRadius));
            var children = depth < 2 ? 3 : 2 + (Random.NextDouble() < 0.45 ? 1 : 0);
            var heading = Vector3.Normalize(end - start);
            var childRadius = tipRadius / MathF.Sqrt(children);
            for (var index = 0; index < children; index++)
            {
                var leader = index == 0;
                var spreadAngle = leader ? 0.25f : 0.45f + 0.35f * (float)Random.NextDouble();
                var (u, v) = PlantGeometry.Basis(heading);
                var azimuth = (float)Random.NextDouble() * MathF.Tau;
                var deviation = u * MathF.Cos(azimuth) + v * MathF.Sin(azimuth);
                var childDirection = Vector3.Normalize(heading * MathF.Cos(spreadAngle) + deviation * MathF.Sin(spreadAngle) +
                                                       Envelope.Outward(end) * 0.25f + Vector3.UnitZ * UpwardBias);
                var childLength = length * (leader ? 0.82f : 0.62f + 0.18f * (float)Random.NextDouble());
                var childStart = leader ? end : Vector3.Lerp(middle, end, 0.4f + 0.6f * (float)Random.NextDouble());
                Branch(childStart, childDirection, childLength, leader ? tipRadius * 0.82f : childRadius, depth + 1);
            }
            // Inner twigs keep the crown dense rather than hollow.
            if (depth >= MaxDepth - 2) LeafCluster(middle, heading, length * 0.6f, LeavesPerTwig / 3);
        }

        /// <summary>
        /// Space colonisation, simplified: leaf clusters are scattered through the outer shell of the crown (where a real
        /// crown carries its leaves) and each is reached by a twig from the nearest branch tip, so the crown is full
        /// whatever the branching produced, with natural gaps between clusters.
        /// </summary>
        public void FillCrown(int clusters, float shell, bool upperOnly = false)
        {
            if (_tips.Count == 0) return;
            for (var index = 0; index < clusters; index++)
            {
                Vector3 point;
                var attempts = 0;
                do
                {
                    var direction = Vector3.Normalize(new Vector3(PlantGeometry.Jitter(Random, 1), PlantGeometry.Jitter(Random, 1),
                        PlantGeometry.Jitter(Random, 1) + 0.25f) + new Vector3(0, 0, 1e-4f));
                    if (upperOnly && direction.Z < -0.35f) direction.Z = -direction.Z * 0.5f;
                    var radial = shell + (1 - shell) * MathF.Sqrt((float)Random.NextDouble());
                    point = Envelope.Center + direction * Envelope.Radii * radial;
                } while (++attempts < 4 && point.Z < Envelope.Center.Z - Envelope.Radii.Z * 0.95f);
                var nearest = _tips[0];
                var best = float.MaxValue;
                foreach (var tip in _tips)
                {
                    var distance = Vector3.DistanceSquared(tip.Position, point);
                    if (distance < best) { best = distance; nearest = tip; }
                }
                var reach = MathF.Sqrt(best);
                // Long straight twigs would draw a wire frame through the crown; far clusters hang on hidden twigs.
                if (reach > 0.05f && reach < 1.2f)
                    Tube(nearest.Position, point, MathF.Max(0.004f, MathF.Min(nearest.Radius, 0.012f)), 0.003f, SurfaceKind.Bark, 4);
                LeafCluster(point, Vector3.Normalize(point - nearest.Position + new Vector3(0, 0, 1e-4f)), 0.4f);
            }
        }

        /// <summary>A cloud of single leaves around a twig's end, each facing roughly up and outward.</summary>
        public void LeafCluster(Vector3 tip, Vector3 heading, float twigLength, int? count = null)
        {
            var leaves = count ?? LeavesPerTwig;
            var radius = MathF.Max(LeafSize * 2.2f, MathF.Min(0.35f, twigLength * 0.8f));
            for (var index = 0; index < leaves; index++)
            {
                var offset = new Vector3(PlantGeometry.Jitter(Random, 1), PlantGeometry.Jitter(Random, 1), PlantGeometry.Jitter(Random, 1));
                if (offset.LengthSquared() > 1) offset = Vector3.Normalize(offset);
                var position = tip + offset * radius - heading * (radius * 0.3f * (float)Random.NextDouble());
                var facing = Vector3.Normalize(Vector3.UnitZ * 0.7f + Envelope.Outward(position) * 0.6f +
                                               new Vector3(PlantGeometry.Jitter(Random, 0.9f), PlantGeometry.Jitter(Random, 0.9f), PlantGeometry.Jitter(Random, 0.5f)));
                var size = LeafSize * (0.75f + 0.5f * (float)Random.NextDouble());
                Leaf(position, facing, Vector3.Normalize(offset + heading * 0.5f + new Vector3(0, 0, 0.0001f)), size);
            }
        }

        /// <summary>An ovate leaf folded slightly along its midrib: six triangles.</summary>
        public void Leaf(Vector3 basePoint, Vector3 normal, Vector3 along, float length)
        {
            along -= normal * Vector3.Dot(normal, along);
            if (along.LengthSquared() < 1e-8f) (along, _) = PlantGeometry.Basis(normal);
            along = Vector3.Normalize(along);
            var side = Vector3.Cross(normal, along);
            Vector3 P(float x, float y, float lift) => basePoint + along * (x * length) + side * (y * length * LeafWidth) + normal * (lift * length);
            if (length < 0.08f)
            {
                // Small leaves and needle blades: a folded diamond of four triangles is all a pixel can resolve.
                var nb = P(0, 0, 0);
                var nl = P(0.45f, 0.24f, 0.06f);
                var nt = P(1, 0, 0.02f);
                var nr = P(0.45f, -0.24f, 0.06f);
                var nm = P(0.45f, 0, 0);
                mesh.AddTriangle(nb, nl, nm, SurfaceKind.Leaf);
                mesh.AddTriangle(nl, nt, nm, SurfaceKind.Leaf);
                mesh.AddTriangle(nb, nm, nr, SurfaceKind.Leaf);
                mesh.AddTriangle(nm, nt, nr, SurfaceKind.Leaf);
                return;
            }
            var b = P(0, 0, 0);
            var l1 = P(0.30f, 0.22f, 0.07f);
            var l2 = P(0.68f, 0.17f, 0.06f);
            var t = P(1, 0, 0.02f);
            var r2 = P(0.68f, -0.17f, 0.06f);
            var r1 = P(0.30f, -0.22f, 0.07f);
            var m = P(0.5f, 0, 0);
            mesh.AddTriangle(b, l1, m, SurfaceKind.Leaf);
            mesh.AddTriangle(l1, l2, m, SurfaceKind.Leaf);
            mesh.AddTriangle(l2, t, m, SurfaceKind.Leaf);
            mesh.AddTriangle(b, m, r1, SurfaceKind.Leaf);
            mesh.AddTriangle(r1, m, r2, SurfaceKind.Leaf);
            mesh.AddTriangle(r2, m, t, SurfaceKind.Leaf);
        }

        /// <summary>A spray of needles along a shoot: thin pairs of slivers angled forward on both sides.</summary>
        public void NeedleSpray(Vector3 from, Vector3 to)
        {
            // Needles are far thinner than a pixel; what reads is the spray they form, so each spray is a dense cluster
            // of narrow leaf blades along the shoot.
            var axis = to - from;
            var length = axis.Length();
            if (length < 1e-4f) return;
            var steps = Math.Max(1, (int)(length / 0.14f));
            for (var step = 0; step <= steps; step++)
                LeafCluster(Vector3.Lerp(from, to, (float)step / steps), axis / length, 0.32f, LeavesPerTwig);
        }

        /// <summary>A palm frond: an arching rachis with long pinnae on both sides forming a shallow V.</summary>
        public void Frond(Vector3 crown, Vector3 outward, float rise, float length)
        {
            var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, outward));
            Vector3 Spine(float t) => crown + outward * (length * t) + new Vector3(0, 0, length * (rise * 0.5f * t - 0.55f * t * t));
            const int steps = 12;
            for (var step = 0; step < steps; step++)
                Tube(Spine((float)step / steps), Spine((float)(step + 1) / steps), 0.025f * (1 - 0.8f * step / steps),
                    0.025f * (1 - 0.8f * (step + 1) / steps), SurfaceKind.Bark, 4);
            const int pinnae = 52;
            for (var index = 2; index < pinnae; index++)
            {
                var t = (float)index / pinnae;
                var at = Spine(t);
                var forward = Vector3.Normalize(Spine(MathF.Min(1, t + 0.02f)) - Spine(MathF.Max(0, t - 0.02f)));
                var leafLength = length * 0.42f * MathF.Sin(MathF.PI * MathF.Min(1, t * 1.1f + 0.05f)) + 0.05f;
                foreach (var sign in new[] { -1f, 1f })
                {
                    var direction = Vector3.Normalize(side * sign * 0.85f + forward * 0.45f + new Vector3(0, 0, 0.18f));
                    var droop = new Vector3(0, 0, -leafLength * 0.35f);
                    var middle = at + direction * (leafLength * 0.5f) + droop * 0.35f;
                    var tip = at + direction * leafLength + droop;
                    var width = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitZ)) * 0.022f;
                    mesh.AddTriangle(at - width, at + width, middle + width, SurfaceKind.Leaf);
                    mesh.AddTriangle(at - width, middle + width, middle - width, SurfaceKind.Leaf);
                    mesh.AddTriangle(middle - width, middle + width, tip, SurfaceKind.Leaf);
                }
            }
        }

        /// <summary>A smooth-shaded tapered cylinder.</summary>
        public void Tube(Vector3 from, Vector3 to, float startRadius, float endRadius, SurfaceKind kind, int sides)
        {
            var axis = to - from;
            if (axis.LengthSquared() < 1e-10f || startRadius <= 0) return;
            var (u, v) = PlantGeometry.Basis(Vector3.Normalize(axis));
            for (var side = 0; side < sides; side++)
            {
                var a0 = side * MathF.Tau / sides;
                var a1 = (side + 1) * MathF.Tau / sides;
                var n0 = u * MathF.Cos(a0) + v * MathF.Sin(a0);
                var n1 = u * MathF.Cos(a1) + v * MathF.Sin(a1);
                mesh.AddSmoothTriangle(from + n0 * startRadius, from + n1 * startRadius, to + n1 * endRadius, n0, n1, n1, kind);
                mesh.AddSmoothTriangle(from + n0 * startRadius, to + n1 * endRadius, to + n0 * endRadius, n0, n1, n0, kind);
            }
        }

        /// <summary>A small smooth sphere (a coconut).</summary>
        public void Sphere(Vector3 center, float radius, SurfaceKind kind)
        {
            const int rings = 6, segments = 10;
            for (var ring = 0; ring < rings; ring++)
                for (var segment = 0; segment < segments; segment++)
                {
                    Vector3 N(int r, int s) => new(
                        MathF.Sin(MathF.PI * r / rings) * MathF.Cos(MathF.Tau * s / segments),
                        MathF.Sin(MathF.PI * r / rings) * MathF.Sin(MathF.Tau * s / segments),
                        MathF.Cos(MathF.PI * r / rings));
                    var a = N(ring, segment);
                    var b = N(ring + 1, segment);
                    var c = N(ring + 1, segment + 1);
                    var d = N(ring, segment + 1);
                    mesh.AddSmoothTriangle(center + a * radius, center + b * radius, center + c * radius, a, b, c, kind);
                    mesh.AddSmoothTriangle(center + a * radius, center + c * radius, center + d * radius, a, c, d, kind);
                }
        }
    }
}
