namespace Guanacaste3D.Core;

/// <summary>Door and window placement plus type management; each public method is one transaction.</summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Places a door or window in a host wall. With an explicit type the type
    /// decides width and height. With only a size, an existing type of that size
    /// is reused or a new one is created. With neither, the kind's default type
    /// is used. The sill defaults to the type's default sill.
    /// </summary>
    public Guid AddOpening(Guid wallId, OpeningKind kind, double? offset = null,
        double? width = null, double? sill = null, double? height = null, Guid? openingTypeId = null,
        bool flipHand = false, bool flipFacing = false)
    {
        var wall = Document.Walls.FirstOrDefault(w => w.Id == wallId)
            ?? throw new InvalidDataException("The host wall does not exist.");
        if (openingTypeId is Guid requestedId)
        {
            var requested = Document.OpeningTypes.FirstOrDefault(type => type.Id == requestedId);
            if (requested is null || requested.Kind != kind)
                throw new InvalidDataException("The selected door or window type does not exist.");
            if ((width is double w && Math.Abs(w - requested.Width) > OpeningTypeRules.SizeTolerance) ||
                (height is double h && Math.Abs(h - requested.Height) > OpeningTypeRules.SizeTolerance))
                throw new InvalidDataException("The requested size conflicts with the selected type.");
        }
        var id = Guid.NewGuid();
        Commit("Transaction.AddOpening", doc =>
        {
            var type = openingTypeId is Guid typeId
                ? doc.OpeningTypes.Single(candidate => candidate.Id == typeId)
                : ResolveType(doc, kind, width, height, sill);
            doc.Openings.Add(new WallOpening(id, wallId, kind, offset ?? wall.Length / 2,
                type.Width, sill ?? type.DefaultSill, type.Height)
            { OpeningTypeId = type.Id, FlipHand = flipHand, FlipFacing = flipFacing });
        });
        return id;
    }

    /// <summary>Sets which jamb a door is hinged on and which face of the wall it swings toward.</summary>
    public void FlipOpening(Guid id, bool flipHand, bool flipFacing)
    {
        if (!Document.Openings.Any(candidate => candidate.Id == id))
            throw new InvalidDataException("The opening does not exist.");
        Commit("Transaction.FlipOpening", doc =>
        {
            var index = doc.Openings.FindIndex(candidate => candidate.Id == id);
            doc.Openings[index] = doc.Openings[index] with { FlipHand = flipHand, FlipFacing = flipFacing };
        });
    }

    /// <summary>
    /// Edits position, sill, and size of one opening. A size change rebinds the
    /// instance to a type of that size, creating one when needed, so editing an
    /// instance never silently resizes other doors that share its old type.
    /// </summary>
    public void UpdateOpening(Guid id, double offset, double width, double sill, double height)
    {
        if (!Document.Openings.Any(o => o.Id == id))
            throw new InvalidDataException("The opening does not exist.");
        Commit("Transaction.UpdateOpening", doc =>
        {
            var index = doc.Openings.FindIndex(o => o.Id == id);
            var opening = doc.Openings[index];
            var current = doc.OpeningTypes.FirstOrDefault(type => type.Id == opening.OpeningTypeId);
            var type = current is not null &&
                       Math.Abs(current.Width - width) <= OpeningTypeRules.SizeTolerance &&
                       Math.Abs(current.Height - height) <= OpeningTypeRules.SizeTolerance
                ? current
                : OpeningTypeRules.FindOrCreate(doc.OpeningTypes, opening.Kind, width, height, sill, current);
            doc.Openings[index] = opening with
            {
                Offset = offset, Sill = sill, Width = type.Width, Height = type.Height, OpeningTypeId = type.Id
            };
        });
    }

    /// <summary>
    /// Moves an opening along its host or into another wall on the same level. The move is one
    /// transaction: an offset that overlaps a neighbour or leaves the wall rejects it and nothing changes.
    /// </summary>
    public void MoveOpening(Guid id, Guid wallId, double offset) => MoveOpening(id, wallId, offset, null);

    /// <summary>Changes the host, offset, and optional sill together; an invalid placement leaves geometry and history untouched.</summary>
    public void MoveOpening(Guid id, Guid wallId, double offset, double? sill)
    {
        var opening = Document.Openings.FirstOrDefault(candidate => candidate.Id == id)
            ?? throw new InvalidDataException("The opening does not exist.");
        var target = Document.Walls.FirstOrDefault(wall => wall.Id == wallId)
            ?? throw new InvalidDataException("The host wall does not exist.");
        var host = Document.Walls.Single(wall => wall.Id == opening.WallId);
        if (target.LevelId != host.LevelId)
            throw new InvalidDataException("The opening can only move to a wall on its level.");
        Commit("Transaction.MoveOpening", doc =>
        {
            var index = doc.Openings.FindIndex(candidate => candidate.Id == id);
            doc.Openings[index] = doc.Openings[index] with { WallId = wallId, Offset = offset, Sill = sill ?? opening.Sill };
        });
    }

    /// <summary>Assigns another type of the same kind to an opening; its size follows the new type.</summary>
    public void SetOpeningType(Guid openingId, Guid typeId)
    {
        var opening = Document.Openings.FirstOrDefault(candidate => candidate.Id == openingId)
            ?? throw new InvalidDataException("The opening does not exist.");
        if (!Document.OpeningTypes.Any(type => type.Id == typeId && type.Kind == opening.Kind))
            throw new InvalidDataException("The selected door or window type does not exist.");
        Commit("Transaction.SetOpeningType", doc =>
        {
            var index = doc.Openings.FindIndex(candidate => candidate.Id == openingId);
            doc.Openings[index] = doc.Openings[index] with { OpeningTypeId = typeId };
        });
    }

    /// <summary>Creates a door or window type.</summary>
    public Guid AddOpeningType(OpeningKind kind, string familyName, string name, double width, double height,
        double defaultSill, string materialName = "", double depth = 0, double frameWidth = 0.06, int glazingLightCount = 0)
    {
        var id = Guid.NewGuid();
        Commit("Transaction.AddOpeningType", doc => doc.OpeningTypes.Add(new OpeningType(
            id, kind, familyName.Trim(), name.Trim(), width, height, defaultSill)
        {
            MaterialName = materialName.Trim(),
            Depth = depth, FrameWidth = frameWidth, GlazingLightCount = glazingLightCount
        }));
        return id;
    }

    /// <summary>Copies a type under a new name in the same family, the usual way to start a new size.</summary>
    public Guid DuplicateOpeningType(Guid typeId, string newName)
    {
        var source = Document.OpeningTypes.FirstOrDefault(type => type.Id == typeId)
            ?? throw new InvalidDataException("The door or window type does not exist.");
        var id = Guid.NewGuid();
        Commit("Transaction.DuplicateOpeningType", doc =>
            doc.OpeningTypes.Add(source with { Id = id, Name = newName.Trim() }));
        return id;
    }

    /// <summary>
    /// Edits a type. Width and height propagate to every instance during
    /// regeneration; if any instance would no longer fit its host wall, the
    /// whole change is rejected and nothing is modified.
    /// </summary>
    public void UpdateOpeningType(Guid typeId, string familyName, string name, double width, double height,
        double defaultSill, string materialName = "", double frameWidth = 0.06, int glazingLightCount = 0)
    {
        if (!Document.OpeningTypes.Any(type => type.Id == typeId))
            throw new InvalidDataException("The door or window type does not exist.");
        Commit("Transaction.UpdateOpeningType", doc =>
        {
            var index = doc.OpeningTypes.FindIndex(type => type.Id == typeId);
            doc.OpeningTypes[index] = doc.OpeningTypes[index] with
            {
                FamilyName = familyName.Trim(), Name = name.Trim(), Width = width, Height = height,
                DefaultSill = defaultSill, MaterialName = materialName.Trim(),
                FrameWidth = frameWidth, GlazingLightCount = glazingLightCount
            };
        });
    }

    /// <summary>Deletes an unused type; types that still have instances are protected.</summary>
    public void RemoveOpeningType(Guid typeId)
    {
        if (!Document.OpeningTypes.Any(type => type.Id == typeId)) return;
        if (Document.Openings.Any(opening => opening.OpeningTypeId == typeId))
            throw new InvalidDataException("The door or window type is still used by placed openings.");
        Commit("Transaction.RemoveOpeningType", doc => doc.OpeningTypes.RemoveAll(type => type.Id == typeId));
    }

    /// <summary>Removes every door and window type without instances; returns how many were removed.</summary>
    public int PurgeUnusedOpeningTypes()
    {
        var used = Document.Openings.Select(opening => opening.OpeningTypeId).ToHashSet();
        var unused = Document.OpeningTypes.Count(type => !used.Contains(type.Id));
        if (unused > 0)
            Commit("Transaction.PurgeOpeningTypes", doc => doc.OpeningTypes.RemoveAll(type => !used.Contains(type.Id)));
        return unused;
    }

    /// <summary>Returns the number of placed instances per opening type.</summary>
    public static IReadOnlyDictionary<Guid, int> CountOpeningInstances(BuildingDocument document) =>
        document.OpeningTypes.ToDictionary(type => type.Id,
            type => document.Openings.Count(opening => opening.OpeningTypeId == type.Id));

    /// <summary>Picks the type for a placement without an explicit type, creating one for a new size.</summary>
    private static OpeningType ResolveType(BuildingDocument document, OpeningKind kind,
        double? width, double? height, double? sill)
    {
        var fallback = document.OpeningTypes.FirstOrDefault(type => type.Id == BuiltInOpeningTypes.DefaultFor(kind))
            ?? document.OpeningTypes.FirstOrDefault(type => type.Kind == kind);
        if (width is null && height is null && fallback is not null) return fallback;
        var defaults = BuiltInOpeningTypes.CreateDefaults().Single(type => type.Id == BuiltInOpeningTypes.DefaultFor(kind));
        return OpeningTypeRules.FindOrCreate(document.OpeningTypes, kind,
            width ?? fallback?.Width ?? defaults.Width, height ?? fallback?.Height ?? defaults.Height,
            sill ?? fallback?.DefaultSill ?? defaults.DefaultSill);
    }
}
