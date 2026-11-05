namespace Guanacaste3D.Core;

/// <summary>Family import as one undoable transaction on a project.</summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Adds a family's door or window types to the project catalog. The import is one
    /// transaction: if the merged catalog is invalid, nothing changes.
    /// </summary>
    public void ImportOpeningFamily(OpeningFamilyDocument family) =>
        Commit("Transaction.ImportOpeningFamily", document => OpeningFamilyImport.Apply(document, family));

    /// <summary>
    /// Loads a mesh component family (a tree, a car) into the project, replacing an earlier load of the same family so
    /// its instances pick up the new geometry. Returns the family's identifier.
    /// </summary>
    public Guid LoadComponentFamily(FamilyDocument family)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.Mesh is null) throw new InvalidDataException("The family has no mesh to place.");
        family.Validate();
        var loaded = family.Clone();
        Commit("Transaction.LoadComponentFamily", document =>
        {
            document.ComponentFamilies.RemoveAll(existing => existing.Id == loaded.Id);
            document.ComponentFamilies.Add(loaded);
        });
        return loaded.Id;
    }

    /// <summary>
    /// Places an instance of a loaded mesh family at its modeled size: a planting for plant families, a component for
    /// the rest. The plan symbol comes from the family's <c>PlanShape</c> metadata (a residential component shape).
    /// </summary>
    public Guid PlaceComponentFamily(Guid levelId, Guid familyId, Point2 at, double rotation = 0, double scale = 1)
    {
        var family = LoadedComponentFamily(familyId);
        if (!double.IsFinite(scale) || scale <= 0) throw new InvalidDataException("The family scale must be positive.");
        return AddElementFromPreset(levelId, ComponentPreset(family, family.Mesh!.Decode().Size, scale), at, at, rotation, familyId);
    }

    /// <summary>
    /// Places many instances of a loaded mesh family in one undoable step, as Lumion's mass placement does: each with
    /// its own position, turn and size (see <see cref="MassPlacement"/> to lay them along a path or through an area).
    /// </summary>
    public IReadOnlyList<Guid> PlaceComponentFamilies(Guid levelId, Guid familyId, IReadOnlyList<ComponentPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        var family = LoadedComponentFamily(familyId);
        if (placements.Count > MassPlacement.MaximumInstances)
            throw new InvalidDataException("The mass placement would create too many instances; increase the spacing.");
        if (placements.Any(placement => !double.IsFinite(placement.At.X) || !double.IsFinite(placement.At.Y) ||
                                        !double.IsFinite(placement.Rotation) || !double.IsFinite(placement.Scale) ||
                                        placement.Scale <= 0))
            throw new InvalidDataException("The family scale must be positive.");
        if (placements.Count == 0) return [];
        // Decoding the mesh once, and committing once, keeps a placement of hundreds of trees to a single copy of
        // the document instead of one per tree.
        var size = family.Mesh!.Decode().Size;
        var ids = placements.Select(_ => Guid.NewGuid()).ToArray();
        Commit("Transaction.MassPlaceComponents", document =>
        {
            for (var i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                document.StructuralElements.Add(PresetElement(ids[i], document, levelId,
                    ComponentPreset(family, size, placement.Scale), placement.At, placement.At, placement.Rotation, familyId));
            }
        });
        return ids;
    }

    /// <summary>The narrowest planting the model accepts, in meters (see <see cref="ArchitecturalShapes"/>).</summary>
    private const double MinimumPlantingWidth = 0.2;

    private FamilyDocument LoadedComponentFamily(Guid familyId) =>
        Document.ComponentFamilies.FirstOrDefault(candidate => candidate.Id == familyId && candidate.Mesh is not null)
        ?? throw new InvalidDataException("An element refers to a component family that is not loaded.");

    /// <summary>
    /// The section an instance takes at its modeled size times <paramref name="scale"/>: a planting for plant families,
    /// a component for the rest, with its plan symbol from the family's <c>PlanShape</c> metadata.
    /// </summary>
    private static ElementPreset ComponentPreset(FamilyDocument family, System.Numerics.Vector3 modeled, double scale)
    {
        var size = modeled * (float)scale;
        var planting = family.Category == FamilyCategory.Planting;
        // A planting must be at least 0.2 m across; a smaller one (a grass tuft shrunk by the size variation) grows
        // evenly to that minimum instead of failing the whole placement.
        var across = (double)Math.Max(size.X, size.Y);
        // The hair of margin keeps single-precision rounding from landing just under the minimum.
        if (planting && across > 0 && across < MinimumPlantingWidth) size *= (float)(MinimumPlantingWidth * (1 + 1e-6) / across);
        var shape = family.Metadata.TryGetValue("PlanShape", out var text) && Enum.TryParse<ResidentialComponentShape>(text, out var parsed)
            ? parsed
            : ResidentialComponentShape.Box;
        return new ElementPreset(family.Name, planting ? StructuralElementKind.Planting : StructuralElementKind.Component,
            ElementStyle.Default, planting ? Math.Max(size.X, size.Y) : size.X, size.Z, 0, planting ? 0 : size.Y,
            planting ? ResidentialComponentShape.Box : shape,
            planting ? ResidentialComponentCategory.None : ResidentialComponentCategory.Outdoor);
    }

    /// <summary>
    /// Keeps the Render panel's camera, sun and filters with the project. It is view state, like the camera of the 3D
    /// view, so it does not add an undo step or mark the project as edited.
    /// </summary>
    public void StoreRenderSetup(Rendering.RenderSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        setup.Validate();
        Document.RenderSetup = setup;
    }
}
