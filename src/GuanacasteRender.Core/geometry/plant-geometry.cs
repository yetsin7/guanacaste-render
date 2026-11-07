using System.Numerics;

namespace Guanacaste3D.Core.Geometry;

/// <summary>
/// Procedural vegetation for site plantings. A plant is generated from its kind of growth (<see cref="ElementStyle"/>),
/// its overall height, its canopy spread (<see cref="StructuralElement.Width"/>) and its id, which seeds the variation:
/// two trees of the same species never look alike, but one tree always regenerates identically. Canopies are lumpy,
/// smooth-shaded clusters of displaced spheres, so the renderer's foliage pattern and relief read as leaves.
/// </summary>
public static class PlantGeometry
{
    /// <summary>Appends the full plant (trunk, limbs and foliage) with its base at the element's offset above the datum.</summary>
    public static void AppendTo(SceneMesh mesh, StructuralElement plant, double datum)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(plant);
        var random = new Random(Seed(plant.Id));
        var root = new Vector3((float)plant.Start.X, (float)plant.Start.Y, (float)(datum + plant.Offset));
        var height = (float)Math.Max(0.2, plant.Height);
        var spread = (float)Math.Max(0.2, plant.Width);
        var first = mesh.Positions.Count;
        switch (plant.Style)
        {
            case ElementStyle.Guanacaste: AddGuanacaste(mesh, root, height, spread, random); break;
            case ElementStyle.Palm: AddPalm(mesh, root, height, spread, random); break;
            case ElementStyle.Conifer: AddConifer(mesh, root, height, spread, random); break;
            case ElementStyle.Shrub: AddShrub(mesh, root, height, spread, random); break;
            default: AddBroadleaf(mesh, root, height, spread, random); break;
        }
        FitToBox(mesh, first, root, height, spread);
    }

    /// <summary>
    /// Scales the plant just generated so it stands on its base and is exactly as tall and as wide as its parameters:
    /// the random lumps of a canopy must not change the height a schedule reports. Normals follow the inverse scale.
    /// </summary>
    internal static void FitToBox(SceneMesh mesh, int first, Vector3 root, float height, float spread)
    {
        var positions = mesh.Positions;
        if (positions.Count == first) return;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var index = first; index < positions.Count; index++)
        {
            min = Vector3.Min(min, positions[index]);
            max = Vector3.Max(max, positions[index]);
        }
        var across = MathF.Max(MathF.Max(max.X - root.X, root.X - min.X), MathF.Max(max.Y - root.Y, root.Y - min.Y)) * 2;
        var horizontal = across > 1e-4f ? spread / across : 1;
        var vertical = max.Z - min.Z > 1e-4f ? height / (max.Z - min.Z) : 1;
        for (var index = first; index < positions.Count; index++)
        {
            var point = positions[index];
            positions[index] = new Vector3(root.X + (point.X - root.X) * horizontal, root.Y + (point.Y - root.Y) * horizontal,
                root.Z + (point.Z - min.Z) * vertical);
        }
        if (mesh.Normals is not { } normals) return;
        for (var index = first; index < normals.Count; index++)
        {
            var normal = normals[index];
            if (normal == Vector3.Zero) continue;
            var scaled = new Vector3(normal.X / horizontal, normal.Y / horizontal, normal.Z / vertical);
            normals[index] = Vector3.Normalize(scaled);
        }
    }

    /// <summary>
    /// Coarse solids for picking, plan symbols and exchange: an octagonal trunk and an octagonal canopy volume. The
    /// visible geometry is <see cref="AppendTo"/>; these only have to cover it.
    /// </summary>
    public static IEnumerable<ElementPart> ProxyParts(StructuralElement plant)
    {
        var (trunkTop, canopyBottom, trunkRadius) = Proportions(plant.Style, plant.Height, plant.Width);
        if (trunkTop > 0)
            yield return new ElementPart(Octagon(plant.Start, trunkRadius), plant.Offset, plant.Offset + trunkTop,
                SurfaceKind.Bark);
        yield return new ElementPart(Octagon(plant.Start, plant.Width / 2), plant.Offset + canopyBottom,
            plant.Offset + plant.Height, SurfaceKind.Foliage);
    }

    /// <summary>Height of the bare trunk, underside of the canopy and trunk radius for a growth style.</summary>
    public static (double TrunkTop, double CanopyBottom, double TrunkRadius) Proportions(
        ElementStyle style, double height, double spread) => style switch
    {
        ElementStyle.Guanacaste => (height * 0.45, height * 0.42, Math.Clamp(height * 0.055, 0.15, 1.2)),
        ElementStyle.Palm => (height * 0.88, height * 0.70, Math.Clamp(height * 0.018, 0.10, 0.35)),
        ElementStyle.Conifer => (height * 0.20, height * 0.12, Math.Clamp(height * 0.022, 0.06, 0.6)),
        ElementStyle.Shrub => (0, 0, 0),
        _ => (height * 0.45, height * 0.34, Math.Clamp(height * 0.03, 0.08, 0.6))
    };

    // ------------------------------------------------------------------ species

    private static void AddBroadleaf(SceneMesh mesh, Vector3 root, float height, float spread, Random random)
    {
        var (trunkTop, canopyBottom, trunkRadius) = Proportions(ElementStyle.Default, height, spread);
        var top = root + new Vector3(Jitter(random, 0.04f) * height, Jitter(random, 0.04f) * height, (float)trunkTop);
        AddLimb(mesh, root, top, (float)trunkRadius, (float)trunkRadius * 0.7f);
        var radius = spread / 2;
        var canopyHeight = height - (float)canopyBottom;
        var center = root + new Vector3(0, 0, (float)canopyBottom + canopyHeight * 0.5f);
        AddBlob(mesh, center, new Vector3(radius * 0.78f, radius * 0.78f, canopyHeight * 0.42f), random);
        var lobes = 6 + random.Next(3);
        for (var index = 0; index < lobes; index++)
        {
            var angle = (index + Jitter(random, 0.35f)) * MathF.Tau / lobes;
            var lift = 0.15f + 0.55f * (float)random.NextDouble();
            var reach = radius * (0.48f + 0.12f * (float)random.NextDouble());
            var lobeCenter = center + new Vector3(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach,
                (lift - 0.4f) * canopyHeight * 0.5f);
            var size = radius * (0.42f + 0.12f * (float)random.NextDouble());
            AddBlob(mesh, lobeCenter, new Vector3(size, size, size * canopyHeight / spread * 1.5f), random);
            if (index % 2 == 0)
                AddLimb(mesh, top, Vector3.Lerp(top, lobeCenter, 0.75f), (float)trunkRadius * 0.55f, (float)trunkRadius * 0.18f);
        }
        AddBlob(mesh, center + new Vector3(0, 0, canopyHeight * 0.32f),
            new Vector3(radius * 0.55f, radius * 0.55f, canopyHeight * 0.3f), random);
    }

    /// <summary>
    /// Enterolobium cyclocarpum, the guanacaste tree of Costa Rica: a short, massive trunk that opens into a few heavy
    /// limbs, under a very wide and flat umbrella crown that is usually much broader than the tree is tall.
    /// </summary>
    private static void AddGuanacaste(SceneMesh mesh, Vector3 root, float height, float spread, Random random)
    {
        var (trunkTop, _, trunkRadius) = Proportions(ElementStyle.Guanacaste, height, spread);
        var fork = root + new Vector3(0, 0, (float)trunkTop * 0.62f);
        AddLimb(mesh, root, fork, (float)trunkRadius * 1.15f, (float)trunkRadius * 0.85f);
        var radius = spread / 2;
        var crownBase = height * 0.62f;
        var crownThickness = height - crownBase;
        var limbs = 4 + random.Next(2);
        var limbEnds = new List<Vector3>();
        for (var index = 0; index < limbs; index++)
        {
            var angle = (index + Jitter(random, 0.3f)) * MathF.Tau / limbs;
            var reach = radius * (0.38f + 0.12f * (float)random.NextDouble());
            var end = root + new Vector3(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach,
                crownBase + crownThickness * 0.15f);
            var elbow = Vector3.Lerp(fork, end, 0.45f) + new Vector3(0, 0, height * 0.06f);
            AddLimb(mesh, fork, elbow, (float)trunkRadius * 0.62f, (float)trunkRadius * 0.40f);
            AddLimb(mesh, elbow, end, (float)trunkRadius * 0.40f, (float)trunkRadius * 0.12f);
            limbEnds.Add(end);
        }
        // The umbrella: a flat central cap and a ring of overlapping, flattened clumps reaching the full spread.
        AddBlob(mesh, root + new Vector3(0, 0, crownBase + crownThickness * 0.55f),
            new Vector3(radius * 0.62f, radius * 0.62f, crownThickness * 0.42f), random);
        var clumps = 10 + random.Next(4);
        for (var index = 0; index < clumps; index++)
        {
            var angle = (index + Jitter(random, 0.4f)) * MathF.Tau / clumps;
            var reach = radius * (0.58f + 0.12f * (float)random.NextDouble());
            var size = radius * (0.30f + 0.10f * (float)random.NextDouble());
            var center = root + new Vector3(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach,
                crownBase + crownThickness * (0.35f + 0.25f * (float)random.NextDouble()));
            AddBlob(mesh, center, new Vector3(size, size, crownThickness * (0.30f + 0.08f * (float)random.NextDouble())), random);
        }
        foreach (var end in limbEnds)
            AddBlob(mesh, end + new Vector3(0, 0, crownThickness * 0.25f),
                new Vector3(radius * 0.32f, radius * 0.32f, crownThickness * 0.32f), random);
    }

    private static void AddPalm(SceneMesh mesh, Vector3 root, float height, float spread, Random random)
    {
        var (trunkTop, _, trunkRadius) = Proportions(ElementStyle.Palm, height, spread);
        // A gently leaning, slightly curved trunk.
        var leanAngle = (float)random.NextDouble() * MathF.Tau;
        var lean = new Vector3(MathF.Cos(leanAngle), MathF.Sin(leanAngle), 0) * (height * 0.08f);
        const int segments = 6;
        var previous = root;
        for (var index = 1; index <= segments; index++)
        {
            var t = (float)index / segments;
            var point = root + lean * (t * t) + new Vector3(0, 0, (float)trunkTop * t);
            AddLimb(mesh, previous, point, (float)trunkRadius * (1.25f - 0.35f * (t - 1f / segments)),
                (float)trunkRadius * (1.25f - 0.35f * t));
            previous = point;
        }
        var crown = previous;
        AddBlob(mesh, crown + new Vector3(0, 0, (float)trunkRadius), new Vector3((float)trunkRadius * 2.4f), random, roughness: 0.1f);
        var fronds = 11 + random.Next(4);
        for (var index = 0; index < fronds; index++)
        {
            var angle = (index + Jitter(random, 0.3f)) * MathF.Tau / fronds;
            var rise = 0.55f - 1.0f * (float)random.NextDouble();
            AddFrond(mesh, crown, new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0), rise,
                spread * (0.46f + 0.08f * (float)random.NextDouble()));
        }
    }

    private static void AddConifer(SceneMesh mesh, Vector3 root, float height, float spread, Random random)
    {
        var (_, canopyBottom, trunkRadius) = Proportions(ElementStyle.Conifer, height, spread);
        AddLimb(mesh, root, root + new Vector3(0, 0, height * 0.92f), (float)trunkRadius, (float)trunkRadius * 0.25f);
        const int tiers = 5;
        var radius = spread / 2;
        for (var tier = 0; tier < tiers; tier++)
        {
            var t = (float)tier / tiers;
            var baseZ = (float)canopyBottom + (height - (float)canopyBottom) * t * 0.82f;
            var tierRadius = radius * (1 - t * 0.78f) * (0.92f + 0.12f * (float)random.NextDouble());
            var apex = Math.Min(height, baseZ + (height - (float)canopyBottom) * 0.42f);
            AddCone(mesh, root + new Vector3(0, 0, baseZ), tierRadius, apex - baseZ, random);
        }
    }

    private static void AddShrub(SceneMesh mesh, Vector3 root, float height, float spread, Random random)
    {
        var radius = spread / 2;
        AddBlob(mesh, root + new Vector3(0, 0, height * 0.5f), new Vector3(radius * 0.8f, radius * 0.8f, height * 0.5f), random);
        var lobes = 3 + random.Next(2);
        for (var index = 0; index < lobes; index++)
        {
            var angle = (index + Jitter(random, 0.4f)) * MathF.Tau / lobes;
            var size = radius * (0.45f + 0.15f * (float)random.NextDouble());
            AddBlob(mesh, root + new Vector3(MathF.Cos(angle) * radius * 0.45f, MathF.Sin(angle) * radius * 0.45f, size * 0.85f),
                new Vector3(size, size, Math.Min(size, height * 0.45f)), random);
        }
    }

    // ------------------------------------------------------------------ primitives

    /// <summary>A tapered, smooth-shaded cylinder from <paramref name="from"/> to <paramref name="to"/>, closed at both ends.</summary>
    private static void AddLimb(SceneMesh mesh, Vector3 from, Vector3 to, float startRadius, float endRadius)
    {
        var axis = to - from;
        var length = axis.Length();
        if (length < 1e-4f || startRadius <= 0) return;
        axis /= length;
        var (u, v) = Basis(axis);
        const int sides = 10;
        for (var side = 0; side < sides; side++)
        {
            var a0 = side * MathF.Tau / sides;
            var a1 = (side + 1) * MathF.Tau / sides;
            var n0 = u * MathF.Cos(a0) + v * MathF.Sin(a0);
            var n1 = u * MathF.Cos(a1) + v * MathF.Sin(a1);
            var p0 = from + n0 * startRadius;
            var p1 = from + n1 * startRadius;
            var q0 = to + n0 * endRadius;
            var q1 = to + n1 * endRadius;
            mesh.AddSmoothTriangle(p0, p1, q1, n0, n1, n1, SurfaceKind.Bark);
            mesh.AddSmoothTriangle(p0, q1, q0, n0, n1, n0, SurfaceKind.Bark);
            mesh.AddTriangle(from, p1, p0, SurfaceKind.Bark);
            mesh.AddTriangle(to, q0, q1, SurfaceKind.Bark);
        }
    }

    /// <summary>A lumpy, smooth-shaded ellipsoid of foliage: an icosphere whose radius is displaced by 3D noise.</summary>
    private static void AddBlob(SceneMesh mesh, Vector3 center, Vector3 radii, Random random, float roughness = 0.16f)
    {
        // Large clumps get a finer sphere so their silhouette stays round; small ones do not need it.
        var (vertices, triangles) = MathF.Max(radii.X, radii.Y) > 4.0f ? Icosphere.Level3 : Icosphere.Level2;
        var offset = new Vector3((float)random.NextDouble() * 50, (float)random.NextDouble() * 50, (float)random.NextDouble() * 50);
        var frequency = 2.2f / MathF.Max(0.3f, MathF.Max(radii.X, radii.Z));
        var positions = new Vector3[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            var direction = vertices[index];
            var scaled = direction * radii;
            // Only broad lumps are modelled; leaf-scale detail comes from the foliage relief at render time, where
            // it costs no triangles and never looks faceted.
            var bump = 1 + roughness * (2 * Noise(scaled * frequency * 1.3f + offset) - 1) +
                       roughness * 0.35f * (2 * Noise(scaled * frequency * 2.9f + offset * 1.7f) - 1);
            positions[index] = center + scaled * bump;
        }
        var normals = new Vector3[vertices.Length];
        foreach (var (a, b, c) in triangles)
        {
            var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
        }
        for (var index = 0; index < normals.Length; index++)
            normals[index] = normals[index].LengthSquared() > 1e-12f ? Vector3.Normalize(normals[index]) : vertices[index];
        foreach (var (a, b, c) in triangles)
            mesh.AddSmoothTriangle(positions[a], positions[b], positions[c], normals[a], normals[b], normals[c],
                SurfaceKind.Foliage);
    }

    /// <summary>A cone of foliage with a ragged rim, smooth-shaded, open underneath like the skirt of a fir tier.</summary>
    private static void AddCone(SceneMesh mesh, Vector3 baseCenter, float radius, float height, Random random)
    {
        const int sides = 18;
        var apex = baseCenter + new Vector3(0, 0, height);
        var rim = new Vector3[sides];
        var normals = new Vector3[sides];
        var droop = height * 0.12f;
        for (var side = 0; side < sides; side++)
        {
            var angle = side * MathF.Tau / sides;
            var ragged = radius * (0.85f + 0.3f * (float)random.NextDouble());
            rim[side] = baseCenter + new Vector3(MathF.Cos(angle) * ragged, MathF.Sin(angle) * ragged,
                -droop * (float)random.NextDouble());
            normals[side] = Vector3.Normalize(new Vector3(MathF.Cos(angle) * height, MathF.Sin(angle) * height, radius));
        }
        for (var side = 0; side < sides; side++)
        {
            var next = (side + 1) % sides;
            mesh.AddSmoothTriangle(rim[side], rim[next], apex, normals[side], normals[next], Vector3.UnitZ, SurfaceKind.Foliage);
            mesh.AddTriangle(baseCenter, rim[next], rim[side], SurfaceKind.Foliage);
        }
    }

    /// <summary>A palm frond: an arching, tapering V-shaped blade that rises from the crown and droops at the tip.</summary>
    private static void AddFrond(SceneMesh mesh, Vector3 crown, Vector3 outward, float rise, float length)
    {
        const int steps = 7;
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, outward));
        Vector3 Spine(float t) => crown + outward * (length * t) +
                                  new Vector3(0, 0, length * (rise * t - 0.75f * t * t));
        for (var step = 0; step < steps; step++)
        {
            var t0 = (float)step / steps;
            var t1 = (float)(step + 1) / steps;
            var s0 = Spine(t0);
            var s1 = Spine(t1);
            var w0 = length * 0.16f * MathF.Sin(MathF.PI * MathF.Min(1, t0 * 1.15f + 0.08f));
            var w1 = length * 0.16f * MathF.Sin(MathF.PI * MathF.Min(1, t1 * 1.15f + 0.08f));
            var fold = new Vector3(0, 0, -0.35f);
            foreach (var sign in new[] { -1f, 1f })
            {
                var e0 = s0 + (side * sign + fold) * w0;
                var e1 = s1 + (side * sign + fold) * w1;
                if (sign < 0) mesh.AddQuad(s0, e0, e1, s1, SurfaceKind.Foliage);
                else mesh.AddQuad(s0, s1, e1, e0, SurfaceKind.Foliage);
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static IReadOnlyList<Point2> Octagon(Point2 center, double radius) =>
        Enumerable.Range(0, 8).Select(index => new Point2(
            center.X + radius * Math.Cos(index * Math.PI / 4 + Math.PI / 8),
            center.Y + radius * Math.Sin(index * Math.PI / 4 + Math.PI / 8))).ToArray();

    internal static int Seed(Guid id)
    {
        var bytes = id.ToByteArray();
        return BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 4) ^ BitConverter.ToInt32(bytes, 8) ^
               BitConverter.ToInt32(bytes, 12);
    }

    internal static float Jitter(Random random, float amount) => ((float)random.NextDouble() * 2 - 1) * amount;

    internal static (Vector3 U, Vector3 V) Basis(Vector3 axis)
    {
        var helper = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, helper));
        return (u, Vector3.Cross(axis, u));
    }

    /// <summary>Smooth 3D value noise in [0, 1].</summary>
    internal static float Noise(Vector3 p)
    {
        var ix = (int)MathF.Floor(p.X);
        var iy = (int)MathF.Floor(p.Y);
        var iz = (int)MathF.Floor(p.Z);
        var f = p - new Vector3(ix, iy, iz);
        f = f * f * (new Vector3(3) - 2 * f);
        float Corner(int dx, int dy, int dz) => Hash(ix + dx, iy + dy, iz + dz);
        var x00 = Lerp(Corner(0, 0, 0), Corner(1, 0, 0), f.X);
        var x10 = Lerp(Corner(0, 1, 0), Corner(1, 1, 0), f.X);
        var x01 = Lerp(Corner(0, 0, 1), Corner(1, 0, 1), f.X);
        var x11 = Lerp(Corner(0, 1, 1), Corner(1, 1, 1), f.X);
        return Lerp(Lerp(x00, x10, f.Y), Lerp(x01, x11, f.Y), f.Z);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Hash(int x, int y, int z)
    {
        var h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2147483647u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) * (1f / 16777216f);
    }

    /// <summary>A unit icosphere subdivided twice (162 vertices, 320 faces), built once and shared.</summary>
    private static class Icosphere
    {
        public static readonly (Vector3[] Vertices, (int A, int B, int C)[] Triangles) Level2 = Build(2);

        /// <summary>Subdivided three times: 642 vertices, 1280 faces.</summary>
        public static readonly (Vector3[] Vertices, (int A, int B, int C)[] Triangles) Level3 = Build(3);

        private static (Vector3[], (int, int, int)[]) Build(int levels)
        {
            var t = (1 + MathF.Sqrt(5)) / 2;
            var vertices = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
            };
            for (var index = 0; index < vertices.Count; index++) vertices[index] = Vector3.Normalize(vertices[index]);
            var faces = new List<(int, int, int)>
            {
                (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2),
                (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5),
                (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)
            };
            for (var level = 0; level < levels; level++)
            {
                var midpoints = new Dictionary<(int, int), int>();
                int Midpoint(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);
                    if (midpoints.TryGetValue(key, out var existing)) return existing;
                    vertices.Add(Vector3.Normalize((vertices[a] + vertices[b]) / 2));
                    return midpoints[key] = vertices.Count - 1;
                }
                var next = new List<(int, int, int)>(faces.Count * 4);
                foreach (var (a, b, c) in faces)
                {
                    var ab = Midpoint(a, b);
                    var bc = Midpoint(b, c);
                    var ca = Midpoint(c, a);
                    next.Add((a, ab, ca));
                    next.Add((b, bc, ab));
                    next.Add((c, ca, bc));
                    next.Add((ab, bc, ca));
                }
                faces = next;
            }
            return (vertices.ToArray(), faces.ToArray());
        }
    }
}
