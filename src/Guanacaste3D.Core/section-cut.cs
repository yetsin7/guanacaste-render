namespace Guanacaste3D.Core;

/// <summary>Defines a vertical cutting plane whose visible horizontal extent runs between two plan points.</summary>
public sealed record SectionCut(Point2 Start, Point2 End)
{
    /// <summary>Rejects degenerate cuts and unbounded coordinates before persistence or projection.</summary>
    public bool IsValid => double.IsFinite(Start.X) && double.IsFinite(Start.Y) &&
        double.IsFinite(End.X) && double.IsFinite(End.Y) &&
        Math.Max(Math.Max(Math.Abs(Start.X), Math.Abs(Start.Y)), Math.Max(Math.Abs(End.X), Math.Abs(End.Y))) <= 1_000_000 &&
        Start.DistanceTo(End) >= 0.01;
}

public sealed partial class DocumentSession
{
    /// <summary>Creates an independently named section with undoable geometry, visible in the project browser.</summary>
    public Guid AddSectionView(string name, SectionCut cut, SectionDisplayMode mode = SectionDisplayMode.Coordinated)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length is 0 or > 120 || Document.Views.Any(view =>
                string.Equals(view.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidDataException("The view name is not valid or is already in use.");
        if (cut is null || !cut.IsValid) throw new InvalidDataException("Invalid section cutting line.");
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("Invalid section display mode.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddSection", document => document.Views.Add(new ProjectView(id, name,
            ProjectViewKind.Section, null, ViewOrientation.None) { Cut = cut, SectionMode = mode }));
        return id;
    }

    /// <summary>Moves an existing cut without changing its identity, name, or browser references.</summary>
    public void UpdateSectionCut(Guid id, SectionCut cut)
    {
        if (cut is null || !cut.IsValid) throw new InvalidDataException("Invalid section cutting line.");
        var index = Document.Views.FindIndex(view => view.Id == id && view.Kind == ProjectViewKind.Section);
        if (index < 0) throw new InvalidDataException("The section view does not exist.");
        Commit("Transaction.EditSection", document => document.Views[index] = document.Views[index] with { Cut = cut });
    }

    /// <summary>Switches a section between the complete coordinated model and its structural construction only.</summary>
    public void SetSectionDisplayMode(Guid id, SectionDisplayMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("Invalid section display mode.");
        var index = Document.Views.FindIndex(view => view.Id == id && view.Kind == ProjectViewKind.Section);
        if (index < 0) throw new InvalidDataException("The section view does not exist.");
        Commit("Transaction.SetSectionDisplayMode", document =>
            document.Views[index] = document.Views[index] with { SectionMode = mode });
    }
}
