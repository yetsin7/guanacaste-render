using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// A walking person built from a few solid shapes (legs, torso, neck, head, hair, arms with hands, shoes) with about four
/// hundred triangles. The shapes keep their triangles from frame to frame; only the vertices move, so a movie poses people
/// by moving vertices and never rebuilds a scene. The body faces +Y, +X is to its right and +Z up; legs and arms swing about
/// the hip and shoulder as the person walks, and the whole figure bobs a little with each step.
/// </summary>
internal static class PersonRig
{
    private const int Skin = 0, Shirt = 1, Trousers = 2, Hair = 3, Shoes = 4;
    private const float Stride = 1.4f;

    private sealed record Part(Vector3[] Vertices, int[] Indices, int Material, Vector3 Pivot, float Swing, float Reach);

    private static readonly Part[] Parts = Build();

    /// <summary>Triangles of one person, with three vertex numbers each, relative to the person's own vertices.</summary>
    public static readonly int[] Indices = Parts.SelectMany((part, index) => part.Indices.Select(value => value + Offset(index))).ToArray();

    /// <summary>The material (0–4: skin, shirt, trousers, hair, shoes) of each triangle.</summary>
    public static readonly int[] TriangleMaterial = Parts.SelectMany(part => Enumerable.Repeat(part.Material, part.Indices.Length / 3)).ToArray();

    public static int VertexCount { get; } = Parts.Sum(part => part.Vertices.Length);

    private static int Offset(int part) => Parts.Take(part).Sum(item => item.Vertices.Length);

    /// <summary>The five materials of a look: its own skin, shirt, trousers and hair, and dark shoes.</summary>
    public static RenderMaterial[] Materials(int look)
    {
        Vector3[] skin = [new(0.55f, 0.38f, 0.28f), new(0.40f, 0.26f, 0.18f), new(0.75f, 0.58f, 0.46f), new(0.30f, 0.19f, 0.13f), new(0.62f, 0.45f, 0.34f), new(0.80f, 0.64f, 0.52f)];
        Vector3[] shirt = [new(0.60f, 0.08f, 0.08f), new(0.08f, 0.20f, 0.45f), new(0.85f, 0.85f, 0.80f), new(0.10f, 0.38f, 0.18f), new(0.85f, 0.62f, 0.08f), new(0.28f, 0.28f, 0.30f)];
        Vector3[] trousers = [new(0.05f, 0.07f, 0.14f), new(0.03f, 0.03f, 0.03f), new(0.45f, 0.38f, 0.28f), new(0.20f, 0.20f, 0.22f), new(0.08f, 0.10f, 0.18f), new(0.30f, 0.26f, 0.20f)];
        Vector3[] hair = [new(0.02f, 0.02f, 0.02f), new(0.10f, 0.05f, 0.03f), new(0.50f, 0.38f, 0.15f), new(0.45f, 0.45f, 0.45f), new(0.04f, 0.03f, 0.02f), new(0.16f, 0.08f, 0.04f)];
        look = Math.Clamp(look, 0, Actor.Looks - 1);
        return
        [
            new RenderMaterial { Name = "Piel", BaseColor = skin[look], Roughness = 0.6f },
            new RenderMaterial { Name = "Camisa", BaseColor = shirt[look], Roughness = 0.85f },
            new RenderMaterial { Name = "Pantalón", BaseColor = trousers[look], Roughness = 0.85f },
            new RenderMaterial { Name = "Cabello", BaseColor = hair[look], Roughness = 0.7f },
            new RenderMaterial { Name = "Zapatos", BaseColor = new Vector3(0.03f), Roughness = 0.5f }
        ];
    }

    /// <summary>Height of a look relative to the 1.82 m the shapes are built for (looks stand from 1.63 m to 1.78 m).</summary>
    public static float HeightFactor(int look) => 0.90f + 0.018f * Math.Clamp(look, 0, Actor.Looks - 1);

    /// <summary>
    /// The vertices of one person standing at <paramref name="state"/> and walking (or standing, when it is not walking), in
    /// world coordinates, in the same order as <see cref="Indices"/> expects.
    /// </summary>
    public static void Pose(int look, ActorState state, Span<Vector3> output)
    {
        var scale = HeightFactor(look);
        var phase = state.Walking ? state.Distance / Stride * 2 * MathF.PI : 0f;
        var swing = MathF.Sin(phase);
        var bob = state.Walking ? MathF.Abs(MathF.Cos(phase)) * 0.018f : 0f;
        float yaw = state.Heading - MathF.PI / 2, cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
        var next = 0;
        foreach (var part in Parts)
        {
            float angle = swing * part.Swing * part.Reach, c = MathF.Cos(angle), s = MathF.Sin(angle);
            foreach (var vertex in part.Vertices)
            {
                var local = vertex;
                if (part.Swing != 0)
                {
                    var (y, z) = (vertex.Y - part.Pivot.Y, vertex.Z - part.Pivot.Z);
                    local = new Vector3(vertex.X, part.Pivot.Y + y * c - z * s, part.Pivot.Z + y * s + z * c);
                }
                local = new Vector3(local.X * scale, local.Y * scale, (local.Z + bob) * scale);
                output[next++] = state.Position + new Vector3(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos, local.Z);
            }
        }
    }

    // ---------------------------------------------------------------- the shapes

    private static Part[] Build()
    {
        var parts = new List<Part>();
        var leftHip = new Vector3(-0.095f, 0, 0.92f);
        var rightHip = new Vector3(0.095f, 0, 0.92f);
        foreach (var (sign, hip, swing) in new[] { (-1f, leftHip, 1f), (1f, rightHip, -1f) })
        {
            var x = sign * 0.095f;
            parts.Add(Make(Prism(new Vector3(x, 0.0f, 0.07f), new Vector3(x, 0, 0.93f), 0.055f, 0.085f, 1f, 1f), Trousers, hip, swing, 0.5f));
            parts.Add(Make(Prism(new Vector3(x, 0.05f, 0.0f), new Vector3(x, 0.05f, 0.075f), 0.058f, 0.058f, 1f, 1.9f), Shoes, hip, swing, 0.5f));
            var shoulder = new Vector3(sign * 0.225f, 0, 1.43f);
            parts.Add(Make(Prism(new Vector3(sign * 0.245f, 0, 0.84f), new Vector3(sign * 0.225f, 0, 1.43f), 0.036f, 0.052f, 1f, 1f), Shirt, shoulder, -swing, 0.4f));
            parts.Add(Make(Sphere(new Vector3(sign * 0.248f, 0, 0.80f), 0.038f, 0.038f, 0.05f, 6, 4), Skin, shoulder, -swing, 0.4f));
        }
        parts.Add(Make(Prism(new Vector3(0, 0, 0.82f), new Vector3(0, 0, 0.96f), 0.17f, 0.165f, 1.1f, 0.78f), Trousers, Vector3.Zero, 0, 0));
        parts.Add(Make(Prism(new Vector3(0, 0, 0.94f), new Vector3(0, 0, 1.48f), 0.16f, 0.19f, 1.15f, 0.75f), Shirt, Vector3.Zero, 0, 0));
        parts.Add(Make(Prism(new Vector3(0, 0, 1.47f), new Vector3(0, 0, 1.59f), 0.046f, 0.04f, 1f, 1f), Skin, Vector3.Zero, 0, 0));
        parts.Add(Make(Sphere(new Vector3(0, 0.01f, 1.67f), 0.095f, 0.11f, 0.12f, 8, 5), Skin, Vector3.Zero, 0, 0));
        parts.Add(Make(Sphere(new Vector3(0, -0.014f, 1.70f), 0.10f, 0.115f, 0.115f, 8, 4), Hair, Vector3.Zero, 0, 0));
        return [.. parts];
    }

    private static Part Make((Vector3[] Vertices, int[] Indices) shape, int material, Vector3 pivot, float swing, float reach) =>
        new(shape.Vertices, shape.Indices, material, pivot, swing, reach);

    /// <summary>A tapered eight-sided prism from <paramref name="bottom"/> up to <paramref name="top"/>, closed at both ends.</summary>
    private static (Vector3[], int[]) Prism(Vector3 bottom, Vector3 top, float bottomRadius, float topRadius, float scaleX, float scaleY)
    {
        const int sides = 8;
        var vertices = new List<Vector3>();
        for (var ring = 0; ring < 2; ring++)
            for (var side = 0; side < sides; side++)
            {
                var angle = side * 2 * MathF.PI / sides;
                var radius = ring == 0 ? bottomRadius : topRadius;
                vertices.Add((ring == 0 ? bottom : top) + new Vector3(MathF.Cos(angle) * radius * scaleX, MathF.Sin(angle) * radius * scaleY, 0));
            }
        vertices.Add(bottom);
        vertices.Add(top);
        var indices = new List<int>();
        for (var side = 0; side < sides; side++)
        {
            int a = side, b = (side + 1) % sides, c = sides + side, d = sides + (side + 1) % sides;
            indices.AddRange([a, b, d, a, d, c]);
            indices.AddRange([2 * sides, b, a]);
            indices.AddRange([2 * sides + 1, c, d]);
        }
        return ([.. vertices], [.. indices]);
    }

    /// <summary>A lat-long ellipsoid of <paramref name="segments"/> by <paramref name="rings"/> faces.</summary>
    private static (Vector3[], int[]) Sphere(Vector3 centre, float rx, float ry, float rz, int segments, int rings)
    {
        var vertices = new List<Vector3> { centre + new Vector3(0, 0, rz), centre - new Vector3(0, 0, rz) };
        for (var ring = 1; ring < rings; ring++)
        {
            var polar = ring * MathF.PI / rings;
            for (var segment = 0; segment < segments; segment++)
            {
                var azimuth = segment * 2 * MathF.PI / segments;
                vertices.Add(centre + new Vector3(MathF.Sin(polar) * MathF.Cos(azimuth) * rx, MathF.Sin(polar) * MathF.Sin(azimuth) * ry, MathF.Cos(polar) * rz));
            }
        }
        var indices = new List<int>();
        int Ring(int ring, int segment) => 2 + (ring - 1) * segments + segment % segments;
        for (var segment = 0; segment < segments; segment++)
        {
            indices.AddRange([0, Ring(1, segment), Ring(1, segment + 1)]);
            indices.AddRange([1, Ring(rings - 1, segment + 1), Ring(rings - 1, segment)]);
            for (var ring = 1; ring < rings - 1; ring++)
            {
                indices.AddRange([Ring(ring, segment), Ring(ring + 1, segment), Ring(ring + 1, segment + 1)]);
                indices.AddRange([Ring(ring, segment), Ring(ring + 1, segment + 1), Ring(ring, segment + 1)]);
            }
        }
        return ([.. vertices], [.. indices]);
    }
}
