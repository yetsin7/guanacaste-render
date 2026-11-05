using System.Numerics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// A car made of a mesh family (the shipped sedan and coupé): its triangles, vertex normals and surface kinds, with the
/// middle of its floor at the origin and its length along Y. It drives as a rigid body; paint is the only thing a look
/// changes, so the same mesh serves several colours.
/// </summary>
internal sealed class CarTemplate
{
    /// <summary>Whether the car's nose points toward +Y in the mesh (+1) or toward −Y (−1); the shipped cars face −Y.</summary>
    public const float Forward = -1f;

    /// <summary>The model and paint of each of the six car looks; a null paint keeps the model's own.</summary>
    private static readonly (int Model, Vector3? Paint)[] Looks =
    [
        (0, new Vector3(0.62f, 0.62f, 0.60f)), (0, new Vector3(0.012f, 0.012f, 0.014f)), (0, new Vector3(0.42f, 0.025f, 0.025f)),
        (1, null), (1, new Vector3(0.025f, 0.07f, 0.34f)), (0, new Vector3(0.03f, 0.20f, 0.08f))
    ];

    public const int Models = 2;

    public static int ModelOf(int look) => Looks[Math.Clamp(look, 0, Actor.Looks - 1)].Model;

    public Vector3[] Positions { get; }
    public Vector3[] Normals { get; }
    public int[] Indices { get; }
    public SurfaceKind[] Kinds { get; }

    /// <summary>Each wheel's axle: where it crosses the car's side (Y and Z in the car's own frame) and its radius.</summary>
    public IReadOnlyList<(float Y, float Z, float Radius)> Wheels => _wheels;

    private readonly (float Y, float Z, float Radius)[] _wheels;
    private readonly sbyte[] _wheelOfVertex;
    private readonly float[] _wheelX;
    private readonly bool[] _steers;

    /// <summary>The farthest the front wheels turn, in radians (35°).</summary>
    public const float MaximumSteer = 0.61f;

    /// <summary>Whether a wheel is one of the two at the nose, which steer.</summary>
    public bool Steers(int wheel) => _steers[wheel];

    /// <summary>The wheel a vertex turns with (0–3), or −1 for the body.</summary>
    public int WheelOf(int vertex) => _wheelOfVertex[vertex];

    public CarTemplate(FamilyMesh.Decoded mesh)
    {
        var centre = new Vector3((mesh.Min.X + mesh.Max.X) / 2, (mesh.Min.Y + mesh.Max.Y) / 2, mesh.Min.Z);
        Positions = mesh.Positions.Select(point => point - centre).ToArray();
        Normals = mesh.Normals ?? new Vector3[mesh.Positions.Length];
        Indices = mesh.Indices;
        Kinds = mesh.Kinds;
        (_wheels, _wheelOfVertex) = FindWheels();
        // Where each wheel stands across the car (the mean of its own vertices), and which two are at the nose.
        _wheelX = new float[_wheels.Length];
        var owned = new int[_wheels.Length];
        for (var vertex = 0; vertex < Positions.Length; vertex++)
            if (_wheelOfVertex[vertex] is var wheel and >= 0) { _wheelX[wheel] += Positions[vertex].X; owned[wheel]++; }
        for (var wheel = 0; wheel < _wheelX.Length; wheel++) _wheelX[wheel] /= Math.Max(1, owned[wheel]);
        _steers = _wheels.Select(wheel => wheel.Y * Forward > 0).ToArray();
    }

    /// <summary>
    /// The angle the front wheels point at for a curve of <paramref name="turn"/> radians per metre: the bicycle model
    /// (atan of wheelbase × curvature), kept within what a steering wheel can do.
    /// </summary>
    public float SteerAngle(float turn)
    {
        if (_wheels.Length != 4) return 0;
        var wheelbase = _wheels.Max(wheel => wheel.Y) - _wheels.Min(wheel => wheel.Y);
        return Math.Clamp(MathF.Atan(wheelbase * turn), -MaximumSteer, MaximumSteer);
    }

    /// <summary>How far the body leans out of a curve, in radians: more the faster and the tighter (lateral acceleration), within 3.5°.</summary>
    public static float Lean(float turn, float speed) => Math.Clamp(-0.005f * speed * speed * turn, -0.061f, 0.061f);

    /// <summary>
    /// Finds the wheels so they can turn: a tyre is a large connected piece of rubber that touches the ground and is about as tall as
    /// a wheel; every vertex of rubber or metal inside the cylinder of a tyre (its rim, hub and cap) turns with it. A vertex that
    /// also belongs to a triangle of the body is left out, so the body never tears. Returns no wheels unless exactly four are found.
    /// </summary>
    private ((float Y, float Z, float Radius)[] Wheels, sbyte[] Owner) FindWheels()
    {
        var noOwner = Enumerable.Repeat((sbyte)-1, Positions.Length).ToArray();
        var parent = Enumerable.Range(0, Positions.Length).ToArray();
        int Find(int vertex)
        {
            while (parent[vertex] != vertex) { parent[vertex] = parent[parent[vertex]]; vertex = parent[vertex]; }
            return vertex;
        }
        var shared = new bool[Positions.Length];
        var wheelKind = new bool[Positions.Length];
        for (var triangle = 0; triangle < Kinds.Length; triangle++)
        {
            var rubberOrMetal = Kinds[triangle] is SurfaceKind.Rubber or SurfaceKind.Metal;
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = Indices[triangle * 3 + corner];
                if (rubberOrMetal) wheelKind[vertex] = true; else shared[vertex] = true;
            }
            if (Kinds[triangle] == SurfaceKind.Rubber)
            {
                parent[Find(Indices[triangle * 3])] = Find(Indices[triangle * 3 + 1]);
                parent[Find(Indices[triangle * 3])] = Find(Indices[triangle * 3 + 2]);
            }
        }
        var pieces = new Dictionary<int, (int Triangles, Vector3 Min, Vector3 Max)>();
        for (var triangle = 0; triangle < Kinds.Length; triangle++)
        {
            if (Kinds[triangle] != SurfaceKind.Rubber) continue;
            var root = Find(Indices[triangle * 3]);
            var (count, min, max) = pieces.TryGetValue(root, out var known) ? known : (0, new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (var corner = 0; corner < 3; corner++)
            {
                var point = Positions[Indices[triangle * 3 + corner]];
                (min, max) = (Vector3.Min(min, point), Vector3.Max(max, point));
            }
            pieces[root] = (count + 1, min, max);
        }
        var tyres = pieces.Values.Where(piece => piece.Triangles >= 600 && piece.Min.Z < 0.06f && piece.Max.Z - piece.Min.Z is > 0.4f and < 1.1f &&
                                                 piece.Max.X - piece.Min.X < 0.5f).ToArray();
        if (tyres.Length != 4) return (Array.Empty<(float, float, float)>(), noOwner);
        var wheels = tyres.Select(tyre => ((tyre.Min.Y + tyre.Max.Y) / 2, (tyre.Min.Z + tyre.Max.Z) / 2, (tyre.Max.Z - tyre.Min.Z) / 2)).ToArray();
        var owner = noOwner;
        for (var vertex = 0; vertex < Positions.Length; vertex++)
        {
            if (!wheelKind[vertex] || shared[vertex]) continue;
            var point = Positions[vertex];
            for (var index = 0; index < 4; index++)
            {
                var (y, z, radius) = wheels[index];
                if (point.X >= tyres[index].Min.X - 0.12f && point.X <= tyres[index].Max.X + 0.12f &&
                    (point.Y - y) * (point.Y - y) + (point.Z - z) * (point.Z - z) <= radius * radius * 1.06f)
                {
                    owner[vertex] = (sbyte)index;
                    break;
                }
            }
        }
        return (wheels, owner);
    }

    /// <summary>The materials of a look, one for each surface kind the model uses (in the order of <see cref="KindOrder"/>).</summary>
    public RenderMaterial[] Materials(int look)
    {
        var paint = Looks[Math.Clamp(look, 0, Actor.Looks - 1)].Paint;
        return KindOrder().Select(kind =>
        {
            var material = MaterialLibrary.ForKind(kind);
            return paint is { } color && kind is SurfaceKind.CarPaint or SurfaceKind.CarPaintSilver ? material with { BaseColor = color } : material;
        }).ToArray();
    }

    public List<SurfaceKind> KindOrder() => Kinds.Distinct().Order().ToList();

    /// <summary>Writes the car at <paramref name="state"/> into <paramref name="positions"/> and <paramref name="normals"/>.</summary>
    public void Pose(ActorState state, Span<Vector3> positions, Span<Vector3> normals)
    {
        var yaw = state.Heading - Forward * MathF.PI / 2;
        float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
        // A wheel that rolls without slipping turns by the distance covered over its radius, about the axle across the car; the
        // top of the tyre goes the way the car goes.
        var spin = _wheels.Select(wheel => -Forward * state.Distance / wheel.Radius).ToArray();
        // The front wheels turn into the curve about a vertical line through their middle, and the whole car (its wheels
        // stay on the ground, but the body rides on springs) leans a little to the outside about the line it drives along.
        var steer = SteerAngle(state.Turn);
        float steerCos = MathF.Cos(steer), steerSin = MathF.Sin(steer);
        var lean = Lean(state.Turn, state.Speed);
        float leanCos = MathF.Cos(lean), leanSin = MathF.Sin(lean);
        for (var index = 0; index < Positions.Length; index++)
        {
            var (local, normal) = (Positions[index], Normals[index]);
            if (_wheelOfVertex[index] is var wheelIndex and >= 0)
            {
                var (wheelY, wheelZ, _) = _wheels[wheelIndex];
                float c = MathF.Cos(spin[wheelIndex]), s = MathF.Sin(spin[wheelIndex]);
                local = new Vector3(local.X, wheelY + (local.Y - wheelY) * c - (local.Z - wheelZ) * s, wheelZ + (local.Y - wheelY) * s + (local.Z - wheelZ) * c);
                normal = new Vector3(normal.X, normal.Y * c - normal.Z * s, normal.Y * s + normal.Z * c);
                if (_steers[wheelIndex] && steer != 0)
                {
                    float dx = local.X - _wheelX[wheelIndex], dy = local.Y - wheelY;
                    local = new Vector3(_wheelX[wheelIndex] + dx * steerCos - dy * steerSin, wheelY + dx * steerSin + dy * steerCos, local.Z);
                    normal = new Vector3(normal.X * steerCos - normal.Y * steerSin, normal.X * steerSin + normal.Y * steerCos, normal.Z);
                }
            }
            if (lean != 0)
            {
                local = new Vector3(local.X * leanCos + local.Z * leanSin, local.Y, -local.X * leanSin + local.Z * leanCos);
                normal = new Vector3(normal.X * leanCos + normal.Z * leanSin, normal.Y, -normal.X * leanSin + normal.Z * leanCos);
            }
            positions[index] = state.Position + new Vector3(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos, local.Z);
            normals[index] = new Vector3(normal.X * cos - normal.Y * sin, normal.X * sin + normal.Y * cos, normal.Z);
        }
    }
}

/// <summary>
/// All the actors of a project as one block of dynamic geometry: the triangles and materials are fixed, and
/// <see cref="Frame"/> gives the vertices (and, for cars, their smooth normals) at any time of the movie. A scene is built
/// with the actors at their start, and a movie moves them frame by frame. Cars need their meshes, which the caller supplies.
/// </summary>
public sealed class ActorRig
{
    private readonly List<(Actor Actor, int Offset, int Count, CarTemplate? Car)> _blocks = [];

    private ActorRig(IReadOnlyList<Actor> actors, Func<int, FamilyMesh.Decoded?>? carModel)
    {
        var indices = new List<int>();
        var triangleMaterial = new List<int>();
        var materials = new List<RenderMaterial>();
        var templates = new Dictionary<int, CarTemplate?>();
        var offset = 0;
        foreach (var actor in actors)
        {
            var materialOffset = materials.Count;
            if (actor.Kind == ActorKind.Person)
            {
                indices.AddRange(PersonRig.Indices.Select(value => value + offset));
                materials.AddRange(PersonRig.Materials(actor.Look));
                triangleMaterial.AddRange(PersonRig.TriangleMaterial.Select(value => value + materialOffset));
                _blocks.Add((actor, offset, PersonRig.VertexCount, null));
                offset += PersonRig.VertexCount;
                continue;
            }
            var model = CarTemplate.ModelOf(actor.Look);
            if (!templates.TryGetValue(model, out var template))
                templates[model] = template = carModel?.Invoke(model) is { } decoded ? new CarTemplate(decoded) : null;
            if (template is null) continue;
            var order = template.KindOrder();
            indices.AddRange(template.Indices.Select(value => value + offset));
            materials.AddRange(template.Materials(actor.Look));
            triangleMaterial.AddRange(Enumerable.Range(0, template.Kinds.Length).Select(triangle => order.IndexOf(template.Kinds[triangle]) + materialOffset));
            _blocks.Add((actor, offset, template.Positions.Length, template));
            offset += template.Positions.Length;
            HasNormals = true;
        }
        Indices = [.. indices];
        TriangleMaterial = [.. triangleMaterial];
        Materials = [.. materials];
        VertexCount = offset;
    }

    /// <summary>A rig for the actors of a document, or null when the project has none that can be drawn.</summary>
    public static ActorRig? Of(BuildingDocument document, Func<int, FamilyMesh.Decoded?>? carModel = null) => From(Actors.Of(document), carModel);

    /// <summary>
    /// A rig for some actors. <paramref name="carModel"/> gives the mesh of a car model (0 sedan, 1 coupé); without it
    /// cars are left out. Null when no actor can be drawn.
    /// </summary>
    public static ActorRig? From(IReadOnlyList<Actor> actors, Func<int, FamilyMesh.Decoded?>? carModel = null)
    {
        var rig = new ActorRig(actors, carModel);
        return rig.VertexCount == 0 ? null : rig;
    }

    public int[] Indices { get; }
    public int[] TriangleMaterial { get; }
    public RenderMaterial[] Materials { get; }
    public int VertexCount { get; }

    /// <summary>Whether some actor has smooth vertex normals that must turn with it (every car does).</summary>
    public bool HasNormals { get; private set; }

    /// <summary>The number of actors the rig draws (cars without a mesh are left out).</summary>
    public int Count => _blocks.Count;

    /// <summary>Every vertex, and every smooth normal (zero for a flat person), of every actor at a time of the movie.</summary>
    public (Vector3[] Positions, Vector3[] Normals) Frame(double seconds)
    {
        var positions = new Vector3[VertexCount];
        var normals = new Vector3[VertexCount];
        foreach (var (actor, offset, count, car) in _blocks)
        {
            var state = Actors.At(actor, seconds);
            if (car is null) PersonRig.Pose(actor.Look, state, positions.AsSpan(offset, count));
            else car.Pose(state, positions.AsSpan(offset, count), normals.AsSpan(offset, count));
        }
        return (positions, normals);
    }

    /// <summary>Every vertex of every actor at a time of the movie.</summary>
    public Vector3[] Positions(double seconds) => Frame(seconds).Positions;
}
