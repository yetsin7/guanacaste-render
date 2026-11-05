using System.Collections.Concurrent;
using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Conversion between photometric values (candela, lux) and the renderer's relative units.</summary>
public static class RenderUnits
{
    /// <summary>
    /// Lux that correspond to one unit of irradiance in the renderer. Chosen so the default sun (irradiance 2π units)
    /// equals clear-sky noon sunlight of about 100 000 lux; a 1 000 lm downlight therefore reads as the dim lamp it is
    /// next to daylight, and dominates only at dusk or indoors.
    /// </summary>
    public const float LuxPerUnit = 100_000f / (2 * MathF.PI);

    /// <summary>Converts a luminous intensity in candela into renderer units.</summary>
    public static float FromCandela(float candela) => candela / LuxPerUnit;
}

/// <summary>
/// Evaluates explicit lights and chooses among many of them cheaply. Lights are bucketed in a lazily built grid;
/// each cell keeps only the lights that can matter there (range culling) with a sampling table that favors nearer,
/// brighter lights. Cells with few lights are evaluated exhaustively; cells with many use resampled importance
/// sampling (RIS) with a weighted reservoir, so cost per shading point is a few cheap candidate evaluations and a
/// single shadow ray, however many luminaires the building has.
/// </summary>
internal sealed class LightSampler
{
    private sealed class Cell
    {
        public int[] Lights = [];
        public float[] Cumulative = [];   // running proposal probability, last entry is 1
        public float[] Probability = [];  // proposal probability of each light in this cell
    }

    private readonly RenderLight[] _lights;
    private readonly Vector3[] _axis;
    private readonly Vector3[] _tangent;
    private readonly Vector3[] _bitangent;
    private readonly float[] _spotOuter;
    private readonly float[] _spotInner;
    private readonly float _cellSize;
    private readonly Vector3 _origin;
    private readonly float _threshold;
    private readonly ConcurrentDictionary<(int, int, int), Cell> _cells = new();

    /// <summary>Candidates examined per shading point when a cell holds more lights than this.</summary>
    public int Candidates { get; }

    public int Count => _lights.Length;

    /// <summary>A cell with at most this many lights evaluates all of them (exact); more are resampled. Unlimited when resampling is off.</summary>
    public int ExhaustiveLimit => Candidates == int.MaxValue ? int.MaxValue : 4;

    public LightSampler(RenderLight[] lights, Vector3 boundsMin, Vector3 boundsMax, int candidates, float cullThreshold)
    {
        _lights = lights;
        Candidates = Math.Max(1, candidates);
        _threshold = Math.Max(0, cullThreshold);
        _origin = boundsMin;
        var diagonal = (boundsMax - boundsMin).Length();
        _cellSize = Math.Clamp(diagonal / 40f, 1.5f, 8f);
        _axis = new Vector3[lights.Length];
        _tangent = new Vector3[lights.Length];
        _bitangent = new Vector3[lights.Length];
        _spotOuter = new float[lights.Length];
        _spotInner = new float[lights.Length];
        for (var i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            var axis = light.SpotDirection is { } direction && direction.LengthSquared() > 1e-8f
                ? Vector3.Normalize(direction)
                : -Vector3.UnitZ;
            _axis[i] = axis;
            (_tangent[i], _bitangent[i]) = Sampling.Basis(-axis);
            _spotOuter[i] = MathF.Cos(light.SpotOuterDegrees * MathF.PI / 180);
            _spotInner[i] = MathF.Cos(light.SpotInnerDegrees * MathF.PI / 180);
        }
    }

    /// <summary>Radiant intensity of light <paramref name="index"/> toward a point (zero outside its cone), without distance falloff.</summary>
    public Vector3 Intensity(int index, Vector3 towardPoint)
    {
        var light = _lights[index];
        if (light.Ies is { } profile)
        {
            // The luminaire's own frame has straight down as −Z; its axis points along the nadir.
            var local = new Vector3(Vector3.Dot(towardPoint, _tangent[index]), Vector3.Dot(towardPoint, _bitangent[index]),
                Vector3.Dot(towardPoint, -_axis[index]));
            return light.Intensity * RenderUnits.FromCandela(profile.IntensityTowards(local));
        }
        if (light.SpotDirection is null) return light.Intensity;
        var cosine = Vector3.Dot(towardPoint, _axis[index]);
        if (cosine <= _spotOuter[index]) return Vector3.Zero;
        var t = Math.Clamp((cosine - _spotOuter[index]) / MathF.Max(_spotInner[index] - _spotOuter[index], 1e-4f), 0f, 1f);
        return light.Intensity * (t * t * (3 - 2 * t));
    }

    /// <summary>The lights that can matter at a point, when few enough to evaluate all of them (empty array otherwise).</summary>
    public ReadOnlySpan<int> All(Vector3 position, out bool exhaustive)
    {
        var cell = CellFor(position);
        exhaustive = cell.Lights.Length <= ExhaustiveLimit;
        return exhaustive ? cell.Lights : [];
    }

    /// <summary>
    /// Picks one light by resampled importance sampling with a one-slot weighted reservoir. The returned weight is
    /// Σw/(M·p̂) and makes the estimate unbiased: multiply the chosen light's full contribution by it.
    /// </summary>
    public bool Resample(Vector3 origin, Vector3 normal, Vector3 geometric, float diffuseLuminance, ref Sampler sampler,
        out int light, out float weight)
    {
        light = -1;
        weight = 0;
        var cell = CellFor(origin);
        var count = cell.Lights.Length;
        if (count == 0) return false;
        var chosenTarget = 0f;
        var weightSum = 0f;
        for (var m = 0; m < Candidates; m++)
        {
            var slot = Array.BinarySearch(cell.Cumulative, sampler.NextFloat());
            if (slot < 0) slot = ~slot;
            slot = Math.Min(slot, count - 1);
            var index = cell.Lights[slot];
            var target = Unshadowed(index, origin, normal, geometric, diffuseLuminance);
            if (target <= 0) continue;
            var w = target / cell.Probability[slot];
            weightSum += w;
            if (sampler.NextFloat() * weightSum < w)
            {
                light = index;
                chosenTarget = target;
            }
        }
        if (light < 0) return false;
        weight = weightSum / (Candidates * chosenTarget);
        return true;
    }

    public RenderLight this[int index] => _lights[index];

    /// <summary>Number of lights that can matter at a point (after range culling).</summary>
    public int LightsNear(Vector3 position) => CellFor(position).Lights.Length;

    /// <summary>Contribution estimate without visibility: what the reservoir uses to prefer useful lights.</summary>
    private float Unshadowed(int index, Vector3 origin, Vector3 normal, Vector3 geometric, float diffuseLuminance)
    {
        var toLight = _lights[index].Position - origin;
        var distanceSquared = MathF.Max(toLight.LengthSquared(), 1e-4f);
        var direction = toLight / MathF.Sqrt(distanceSquared);
        var cosine = Vector3.Dot(normal, direction);
        if (cosine <= 0 || Vector3.Dot(geometric, direction) <= 0) return 0;
        var intensity = Sampling.Luminance(Intensity(index, -direction));
        return intensity * cosine / distanceSquared * (diffuseLuminance + 0.05f);
    }

    private Cell CellFor(Vector3 position)
    {
        var key = ((int)MathF.Floor((position.X - _origin.X) / _cellSize),
                   (int)MathF.Floor((position.Y - _origin.Y) / _cellSize),
                   (int)MathF.Floor((position.Z - _origin.Z) / _cellSize));
        return _cells.GetOrAdd(key, Build);
    }

    private Cell Build((int X, int Y, int Z) key)
    {
        var min = _origin + new Vector3(key.X, key.Y, key.Z) * _cellSize;
        var max = min + new Vector3(_cellSize);
        var center = (min + max) * 0.5f;
        var indices = new List<int>();
        var proposals = new List<float>();
        for (var i = 0; i < _lights.Length; i++)
        {
            var light = _lights[i];
            var nearest = Vector3.Clamp(light.Position, min, max);
            var distanceSquared = MathF.Max(Vector3.DistanceSquared(light.Position, nearest), 0.01f);
            var peak = light.Ies is { } ies
                ? Sampling.Luminance(light.Intensity) * RenderUnits.FromCandela(ies.PeakCandela)
                : Sampling.Luminance(light.Intensity);
            // Range culling: beyond this distance the light adds less than the threshold irradiance.
            if (peak / distanceSquared < _threshold) continue;
            indices.Add(i);
            var centerDistance = MathF.Max(Vector3.DistanceSquared(light.Position, center), _cellSize * _cellSize * 0.25f);
            proposals.Add(peak / centerDistance);
        }
        var sum = proposals.Sum();
        var cumulative = new float[indices.Count];
        var probability = new float[indices.Count];
        var running = 0f;
        for (var i = 0; i < indices.Count; i++)
        {
            probability[i] = sum > 0 ? proposals[i] / sum : 1f / indices.Count;
            running += probability[i];
            cumulative[i] = running;
        }
        if (cumulative.Length > 0) cumulative[^1] = 1;
        return new Cell { Lights = indices.ToArray(), Cumulative = cumulative, Probability = probability };
    }
}
