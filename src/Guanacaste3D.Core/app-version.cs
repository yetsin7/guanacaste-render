using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>
/// The product version, kept in order by hand: 1.0.0, 1.0.1 … 1.0.99, then 1.1.0 … 1.99.99, then 2.0.0. The minor and the
/// patch each count up to 99 and then carry into the part before them.
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    /// <summary>The largest minor and patch number before the part to the left of it goes up.</summary>
    public const int Limit = 99;

    /// <summary>The version that follows this one.</summary>
    public AppVersion Next() =>
        Patch < Limit ? this with { Patch = Patch + 1 } :
        Minor < Limit ? new AppVersion(Major, Minor + 1, 0) :
        new AppVersion(Major + 1, 0, 0);

    /// <summary>
    /// Reads <c>major.minor.patch</c>, ignoring build metadata after a '+'. Anything else, or a minor or patch above
    /// <see cref="Limit"/>, is not a version of this product.
    /// </summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        var core = text?.Split('+')[0].Trim();
        var parts = core?.Split('.');
        if (parts is not { Length: 3 } ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch) ||
            minor > Limit || patch > Limit)
            return false;
        version = new AppVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(AppVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) :
        Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
