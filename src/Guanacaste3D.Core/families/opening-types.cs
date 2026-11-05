using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>How a door leaf or window sash moves. <see cref="Standard"/> keeps the original per-kind behavior.</summary>
public enum OpeningOperation
{
    /// <summary>Hinged leaf for doors, equal lights for windows.</summary>
    Standard,

    /// <summary>Panels that slide past one another along a track.</summary>
    Sliding,

    /// <summary>Narrow leaves that fold against each other.</summary>
    Folding,

    /// <summary>A leaf that slides into a cavity inside the wall.</summary>
    Pocket,

    /// <summary>Horizontal sections that roll up overhead (garage door).</summary>
    Overhead,

    /// <summary>Four wings turning around a central shaft.</summary>
    Revolving,

    /// <summary>A side-hinged window sash.</summary>
    Casement,

    /// <summary>A top-hinged window sash that opens outward.</summary>
    Awning,

    /// <summary>Two sashes stacked vertically, separated by a meeting rail.</summary>
    Hung,

    /// <summary>Horizontal glass blades (jalousie).</summary>
    Louvered,

    /// <summary>A plain hole through the wall with no frame, leaf or sill.</summary>
    Void
}

/// <summary>How a door leaf is made.</summary>
public enum DoorLeafStyle
{
    /// <summary>Two raised-and-fielded panels.</summary>
    Panelled,
    /// <summary>A smooth flush leaf, as for interior and hotel doors.</summary>
    Flush,
    /// <summary>Six raised panels in three rows, the classic colonial door.</summary>
    SixPanel,
    /// <summary>Angled slats over the whole leaf: ventilation for closets and service rooms.</summary>
    Louvered,
    /// <summary>A frameless tempered-glass leaf.</summary>
    FullGlass,
    /// <summary>A steel security leaf with a grille over glass, as at many Costa Rican entrances.</summary>
    Security
}

/// <summary>
/// A reusable door or window type. Width and height are type parameters that
/// drive every instance; <see cref="DefaultSill"/> seeds the instance sill when
/// an opening is placed. <see cref="FamilyName"/> groups related types the way
/// a family groups its sizes.
/// </summary>
public sealed record OpeningType(
    Guid Id,
    OpeningKind Kind,
    string FamilyName,
    string Name,
    double Width,
    double Height,
    double DefaultSill)
{
    /// <summary>Optional frame or panel material shown in schedules and exports.</summary>
    public string MaterialName { get; init; } = string.Empty;

    /// <summary>
    /// Leaf or panel thickness in meters. Zero means the opening is a plain hole. A
    /// positive depth fills the opening with a leaf no thicker than its host wall.
    /// </summary>
    public double Depth { get; init; }

    /// <summary>Visible lining width in meters; also controls the sash member size.</summary>
    public double FrameWidth { get; init; } = 0.06;

    /// <summary>Window lights; zero derives one or two lights from the opening width.</summary>
    public int GlazingLightCount { get; init; }

    /// <summary>How the leaf or sash operates; drives both the 3D assembly and the plan symbol.</summary>
    public OpeningOperation Operation { get; init; }

    /// <summary>Doors only: the leaf panel is glass instead of solid timber.</summary>
    public bool GlazedLeaf { get; init; }

    /// <summary>Doors: how the leaf is made.</summary>
    public DoorLeafStyle LeafStyle { get; init; }

    /// <summary>Windows: a steel grille of vertical bars on the outside face.</summary>
    public bool SecurityBars { get; init; }

    /// <summary>Returns "Family: Type" for lists and schedules.</summary>
    public string DisplayName => $"{FamilyName}: {Name}";
}

/// <summary>Stable identifiers and defaults for the built-in door and window catalog.</summary>
public static class BuiltInOpeningTypes
{
    public static readonly Guid SingleDoor800Id = Guid.Parse("6f0d6c1e-3c39-4a63-9a1f-0b8e0c6f7a10");
    public static readonly Guid SingleDoor900Id = Guid.Parse("0f3b2f5d-5f0e-4b8c-8f7c-6c3c2d1a9e21");
    public static readonly Guid DoubleDoor1500Id = Guid.Parse("a7d9c3b2-1e4f-4c6a-b8d0-2f5e7a9c1b32");
    public static readonly Guid FixedWindow600Id = Guid.Parse("4c2e8a6f-7b1d-4e3c-9a5f-8d0b2c4e6f43");
    public static readonly Guid FixedWindow1200Id = Guid.Parse("e1b5d7f9-2a4c-4e6b-8d0f-1a3c5e7b9d54");
    public static readonly Guid SlidingWindow1600Id = Guid.Parse("9b3d5f7a-4c6e-4a8b-9d1f-3b5d7f9a1c65");

    /// <summary>Creates an independent default opening-type catalog for a new document.</summary>
    public static List<OpeningType> CreateDefaults() =>
    [
        new(SingleDoor800Id, OpeningKind.Door, "Single Door", "800 × 2100 mm", 0.8, 2.1, 0) { Depth = 0.04 },
        new(SingleDoor900Id, OpeningKind.Door, "Single Door", "900 × 2100 mm", 0.9, 2.1, 0) { Depth = 0.04 },
        new(DoubleDoor1500Id, OpeningKind.Door, "Double Door", "1500 × 2100 mm", 1.5, 2.1, 0) { Depth = 0.04 },
        new(FixedWindow600Id, OpeningKind.Window, "Fixed Window", "600 × 600 mm", 0.6, 0.6, 1.5),
        new(FixedWindow1200Id, OpeningKind.Window, "Fixed Window", "1200 × 1200 mm", 1.2, 1.2, 0.9),
        new(SlidingWindow1600Id, OpeningKind.Window, "Sliding Window", "1600 × 1400 mm", 1.6, 1.4, 0.9)
            { Operation = OpeningOperation.Sliding, GlazingLightCount = 2 }
    ];

    /// <summary>Returns the type used when an opening is placed without an explicit type or size.</summary>
    public static Guid DefaultFor(OpeningKind kind) =>
        kind == OpeningKind.Door ? SingleDoor900Id : FixedWindow1200Id;
}

/// <summary>Validation and lookup rules for opening types.</summary>
public static class OpeningTypeRules
{
    /// <summary>Maximum difference at which two sizes are considered the same type size.</summary>
    public const double SizeTolerance = 0.0000005;

    /// <summary>Rejects an invalid catalog or openings that are out of sync with their types.</summary>
    public static void Validate(BuildingDocument document)
    {
        var types = document.OpeningTypes;
        if (types is null || types.Count > 10000 || types.Any(type => type is null ||
                type.Id == Guid.Empty || !Enum.IsDefined(type.Kind) ||
                string.IsNullOrWhiteSpace(type.FamilyName) || type.FamilyName.Length > 120 ||
                string.IsNullOrWhiteSpace(type.Name) || type.Name.Length > 120 ||
                type.MaterialName is null || type.MaterialName.Length > 120 ||
                !double.IsFinite(type.Width) || type.Width is < 0.3 or > 10 ||
                !double.IsFinite(type.Height) || type.Height is < 0.3 or > 20 ||
                !double.IsFinite(type.DefaultSill) || type.DefaultSill is < 0 or > 50 ||
                !double.IsFinite(type.Depth) || type.Depth is < 0 or > 1 ||
                !double.IsFinite(type.FrameWidth) || type.FrameWidth is < 0.02 or > 0.15 ||
                !Enum.IsDefined(type.Operation) || !Enum.IsDefined(type.LeafStyle) ||
                (type.SecurityBars && type.Kind != OpeningKind.Window) || (type.Kind == OpeningKind.Door && type.Operation is
                    OpeningOperation.Casement or OpeningOperation.Awning or OpeningOperation.Hung or
                    OpeningOperation.Louvered or OpeningOperation.Void) || (type.Kind == OpeningKind.Window && (type.GlazedLeaf || type.Operation is
                    OpeningOperation.Folding or OpeningOperation.Pocket or OpeningOperation.Overhead or
                    OpeningOperation.Revolving)) ||
                type.GlazingLightCount is < 0 or > 4 ||
                type.Width <= 2 * type.FrameWidth + 0.1 ||
                (type.Kind == OpeningKind.Window &&
                    (type.Width - 2 * type.FrameWidth) /
                    (type.GlazingLightCount > 0 ? type.GlazingLightCount : type.Width > 1 ? 2 : 1)
                    <= 2 * Math.Min(type.FrameWidth, 0.07) + 0.02) ||
                (type.Kind == OpeningKind.Door && type.GlazingLightCount != 0) ||
                (type.GlazingLightCount > 0 && type.Width / type.GlazingLightCount < 0.18) ||
                (type.Kind == OpeningKind.Door && type.DefaultSill != 0)))
            throw new InvalidDataException("Invalid door or window type catalog.");
        if (types.Select(type => type.Id).Distinct().Count() != types.Count ||
            types.Select(type => (type.FamilyName.ToUpperInvariant(), type.Name.ToUpperInvariant()))
                .Distinct().Count() != types.Count)
            throw new InvalidDataException("Door and window type identifiers and names must be unique.");
        var byId = types.ToDictionary(type => type.Id);
        foreach (var opening in document.Openings)
        {
            if (!byId.TryGetValue(opening.OpeningTypeId, out var type) || type.Kind != opening.Kind ||
                Math.Abs(type.Width - opening.Width) > SizeTolerance ||
                Math.Abs(type.Height - opening.Height) > SizeTolerance)
                throw new InvalidDataException("An opening references a missing or mismatched type.");
        }
    }

    /// <summary>Regeneration: copies type sizes onto every instance so geometry follows its type.</summary>
    public static void SynchronizeInstances(BuildingDocument document)
    {
        var byId = document.OpeningTypes.ToDictionary(type => type.Id);
        for (var index = 0; index < document.Openings.Count; index++)
        {
            var opening = document.Openings[index];
            if (byId.TryGetValue(opening.OpeningTypeId, out var type) &&
                (opening.Width != type.Width || opening.Height != type.Height))
                document.Openings[index] = opening with { Width = type.Width, Height = type.Height };
        }
    }

    /// <summary>Finds a type of the given kind and size, preferring built-in or older types.</summary>
    public static OpeningType? FindBySize(IEnumerable<OpeningType> types, OpeningKind kind, double width, double height) =>
        types.FirstOrDefault(type => type.Kind == kind &&
            Math.Abs(type.Width - width) <= SizeTolerance && Math.Abs(type.Height - height) <= SizeTolerance);

    /// <summary>
    /// Returns a matching type, adding a new one to <paramref name="types"/> when
    /// no type of that size exists. New types join the default family of the kind.
    /// </summary>
    public static OpeningType FindOrCreate(List<OpeningType> types, OpeningKind kind, double width, double height,
        double defaultSill, OpeningType? basedOn = null)
    {
        if (basedOn is not null)
        {
            // Resizing an instance keeps its family, operation and frame instead of falling back to the default family.
            var sibling = types.FirstOrDefault(type => type.Kind == kind && type.FamilyName == basedOn.FamilyName &&
                Math.Abs(type.Width - width) <= SizeTolerance && Math.Abs(type.Height - height) <= SizeTolerance);
            if (sibling is not null) return sibling;
            var siblingName = string.Create(CultureInfo.InvariantCulture, $"{width * 1000:0} × {height * 1000:0} mm");
            for (var suffix = 2; types.Any(type => type.FamilyName == basedOn.FamilyName && type.Name == siblingName); suffix++)
                siblingName = string.Create(CultureInfo.InvariantCulture, $"{width * 1000:0} × {height * 1000:0} mm ({suffix})");
            var copy = basedOn with { Id = Guid.NewGuid(), Name = siblingName, Width = width, Height = height,
                DefaultSill = kind == OpeningKind.Door ? 0 : defaultSill };
            types.Add(copy);
            return copy;
        }
        if (FindBySize(types, kind, width, height) is { } existing) return existing;
        var family = kind == OpeningKind.Door ? "Single Door" : "Fixed Window";
        var name = string.Create(CultureInfo.InvariantCulture, $"{width * 1000:0} × {height * 1000:0} mm");
        for (var suffix = 2; types.Any(type => type.FamilyName == family && type.Name == name); suffix++)
            name = string.Create(CultureInfo.InvariantCulture, $"{width * 1000:0} × {height * 1000:0} mm ({suffix})");
        var created = new OpeningType(Guid.NewGuid(), kind, family, name, width, height,
            kind == OpeningKind.Door ? 0 : defaultSill) { Depth = kind == OpeningKind.Door ? 0.04 : 0 };
        types.Add(created);
        return created;
    }
}
