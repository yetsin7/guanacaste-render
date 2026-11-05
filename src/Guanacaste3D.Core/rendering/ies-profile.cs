using System.Globalization;
using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// A luminaire's measured light distribution from an IESNA LM-63 file. Only photometric type C (the type used for
/// building luminaires) is supported; the others are rejected rather than guessed at.
/// </summary>
public sealed class IesProfile
{
    // Bound work before splitting untrusted LM-63 text into lines and tokens.
    public const int MaxIesCharacters = 8 * 1024 * 1024;
    private readonly float[] _vertical;
    private readonly float[] _horizontal;
    private readonly float[,] _candela; // [horizontal, vertical]

    private IesProfile(float[] vertical, float[] horizontal, float[,] candela, string title)
    {
        _vertical = vertical;
        _horizontal = horizontal;
        _candela = candela;
        Title = title;
        LuminousFluxLumens = IntegrateFlux();
        if (!float.IsFinite(LuminousFluxLumens))
            throw new InvalidDataException("The IES luminous flux exceeds the supported range.");
    }

    public string Title { get; }

    /// <summary>Total emitted light in lumens, found by integrating the candela table over the sphere.</summary>
    public float LuminousFluxLumens { get; }

    /// <summary>Largest candela value in the table.</summary>
    public float PeakCandela => _candela.Cast<float>().DefaultIfEmpty(0).Max();

    /// <summary>Parses LM-63 text. Throws <see cref="InvalidDataException"/> for files that are malformed or unsupported.</summary>
    public static IesProfile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxIesCharacters)
            throw new InvalidDataException("The IES file is too large.");
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var index = 0;
        var title = string.Empty;
        for (; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("[TEST]", StringComparison.OrdinalIgnoreCase) && title.Length == 0)
                title = line[6..].Trim();
            if (line.StartsWith("[LUMCAT]", StringComparison.OrdinalIgnoreCase) && title.Length == 0)
                title = line[8..].Trim();
            if (line.StartsWith("TILT=", StringComparison.OrdinalIgnoreCase)) break;
        }
        if (index >= lines.Length) throw new InvalidDataException("The IES file has no TILT line.");
        var tilt = lines[index].Trim()[5..].Trim();
        index++;

        var numbers = new List<float>();
        for (; index < lines.Length; index++)
            foreach (var token in lines[index].Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                    throw new InvalidDataException("The IES file contains a value that is not a number.");
                if (numbers.Count >= 2_000_000)
                    throw new InvalidDataException("The IES photometric table is too large.");
                numbers.Add(value);
            }

        var cursor = 0;
        float Next() => cursor < numbers.Count ? numbers[cursor++] : throw new InvalidDataException("The IES file ends early.");

        if (tilt.Equals("INCLUDE", StringComparison.OrdinalIgnoreCase))
        {
            Next(); // lamp to luminaire geometry
            var tiltCount = (int)Next();
            if (tiltCount < 0 || tiltCount > 1000) throw new InvalidDataException("The IES tilt table is not valid.");
            cursor += tiltCount * 2;
        }
        else if (!tilt.Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The IES tilt reference is not supported.");
        }

        Next(); // number of lamps
        Next(); // lumens per lamp (informational: candela values are absolute)
        var multiplier = Next();
        var verticalCount = (int)Next();
        var horizontalCount = (int)Next();
        var photometricType = (int)Next();
        Next(); // units type
        Next(); Next(); Next(); // luminous opening width, length, height
        Next(); Next(); Next(); // ballast factor, future use, input watts
        if (photometricType != 1) throw new InvalidDataException("Only photometric type C IES files are supported.");
        if (verticalCount is < 2 or > 4096 || horizontalCount is < 1 or > 4096 || !float.IsFinite(multiplier) || multiplier < 0)
            throw new InvalidDataException("The IES angle table is not valid.");

        var vertical = new float[verticalCount];
        for (var i = 0; i < verticalCount; i++) vertical[i] = Next();
        var horizontal = new float[horizontalCount];
        for (var i = 0; i < horizontalCount; i++) horizontal[i] = Next();
        if (!Ascending(vertical) || !Ascending(horizontal) || vertical[0] < 0 || vertical[^1] > 180 || horizontal[0] < 0 || horizontal[^1] > 360)
            throw new InvalidDataException("The IES angles are out of order or out of range.");
        var candela = new float[horizontalCount, verticalCount];
        for (var h = 0; h < horizontalCount; h++)
            for (var v = 0; v < verticalCount; v++)
            {
                var value = Next() * multiplier;
                if (!float.IsFinite(value) || value < 0)
                    throw new InvalidDataException("The IES candela table contains an invalid intensity.");
                candela[h, v] = value;
            }
        return new IesProfile(vertical, horizontal, candela, title);
    }

    /// <summary>
    /// Intensity in candela toward a direction given in the luminaire's own frame, where straight down is −Z
    /// (vertical angle 0°) and +X is horizontal angle 0°.
    /// </summary>
    public float IntensityTowards(Vector3 localDirection)
    {
        var length = localDirection.Length();
        if (!float.IsFinite(length) || length < 1e-8f) return 0;
        var direction = localDirection / length;
        var vertical = MathF.Acos(Math.Clamp(-direction.Z, -1f, 1f)) * 180f / MathF.PI;
        var horizontal = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
        if (horizontal < 0) horizontal += 360;
        return Candela(vertical, horizontal);
    }

    /// <summary>Bilinear lookup that applies the symmetry the file declares for its horizontal angles.</summary>
    public float Candela(float verticalDegrees, float horizontalDegrees)
    {
        if (!float.IsFinite(verticalDegrees) || !float.IsFinite(horizontalDegrees)) return 0;
        var v = Math.Clamp(verticalDegrees, _vertical[0], _vertical[^1]);
        var h = FoldHorizontal(horizontalDegrees);
        var (v0, v1, vt) = Bracket(_vertical, v);
        var (h0, h1, ht) = Bracket(_horizontal, h);
        var low = _candela[h0, v0] * (1 - vt) + _candela[h0, v1] * vt;
        var high = _candela[h1, v0] * (1 - vt) + _candela[h1, v1] * vt;
        return low * (1 - ht) + high * ht;
    }

    private float FoldHorizontal(float degrees)
    {
        if (_horizontal.Length == 1) return _horizontal[0];
        var angle = degrees % 360;
        if (angle < 0) angle += 360;
        var last = _horizontal[^1];
        if (last >= 359.99f) return angle;
        if (last >= 179.99f) return angle > 180 ? 360 - angle : angle;      // bilateral symmetry
        if (last >= 89.99f)                                                   // quadrant symmetry
        {
            if (angle > 180) angle = 360 - angle;
            return angle > 90 ? 180 - angle : angle;
        }
        return Math.Clamp(angle, _horizontal[0], last);
    }

    private static (int Low, int High, float Fraction) Bracket(float[] angles, float value)
    {
        if (angles.Length == 1) return (0, 0, 0);
        // Parsed angle tables are strictly ascending. Binary search keeps IES
        // flux integration responsive even for dense manufacturer photometry.
        var high = Array.BinarySearch(angles, value);
        if (high < 0) high = ~high; // first angle greater than the requested one
        if (high >= angles.Length) return (angles.Length - 1, angles.Length - 1, 0);
        if (high == 0) return (0, 1, 0);
        var low = high - 1;
        var span = angles[high] - angles[low];
        return (low, high, span <= 1e-6f ? 0 : (value - angles[low]) / span);
    }

    /// <summary>Integrates intensity over the sphere on a regular grid, which is accurate for smooth distributions.</summary>
    private float IntegrateFlux()
    {
        const int thetaSteps = 180;
        const int phiSteps = 72;
        double sum = 0;
        for (var i = 0; i < thetaSteps; i++)
        {
            var theta = (i + 0.5) * 180.0 / thetaSteps;
            var weight = Math.Sin(theta * Math.PI / 180) * (Math.PI / thetaSteps) * (2 * Math.PI / phiSteps);
            for (var j = 0; j < phiSteps; j++)
                sum += Candela((float)theta, (float)((j + 0.5) * 360.0 / phiSteps)) * weight;
        }
        return (float)sum;
    }

    private static bool Ascending(float[] values)
    {
        for (var i = 1; i < values.Length; i++)
            if (values[i] <= values[i - 1]) return false;
        return true;
    }
}
