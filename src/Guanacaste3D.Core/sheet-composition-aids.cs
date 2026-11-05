namespace Guanacaste3D.Core;

/// <summary>What a view reference says: the name of the view it points to and the number of the sheet that shows it.</summary>
public static class ViewReferences
{
    /// <summary>The view whose name is, or begins with or contains, the text; an exact name wins. Schedules and the 3D view can be referred to as well.</summary>
    public static ProjectView? Find(BuildingDocument document, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var wanted = text.Trim();
        return document.Views.FirstOrDefault(view => view.Name.Equals(wanted, StringComparison.CurrentCultureIgnoreCase)) ??
               document.Views.FirstOrDefault(view => view.Name.StartsWith(wanted, StringComparison.CurrentCultureIgnoreCase)) ??
               document.Views.FirstOrDefault(view => view.Name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>The numbers of the sheets that show the view, in order.</summary>
    public static IReadOnlyList<string> SheetNumbers(BuildingDocument document, Guid viewId) =>
        document.Sheets.Where(sheet => sheet.Viewports.Any(viewport => viewport.ViewId == viewId)).Select(sheet => sheet.Number).Order().ToList();

    /// <summary>"Elevation South · A-101", or the view's name alone when no sheet shows it.</summary>
    public static string Label(BuildingDocument document, ProjectView view)
    {
        var sheets = SheetNumbers(document, view.Id);
        return sheets.Count == 0 ? view.Name : $"{view.Name} · {string.Join(", ", sheets)}";
    }
}

public sealed partial class DocumentSession
{
    /// <summary>Sets the spacing of the guide grid of a sheet, in sheet millimeters between 5 and 100, or 0 to remove it.</summary>
    public void SetSheetGuideGrid(Guid sheetId, double spacingMm)
    {
        if (!double.IsFinite(spacingMm) || (spacingMm != 0 && spacingMm is < 5 or > 100)) throw new InvalidDataException("The guide grid spacing is invalid.");
        if (Document.Sheets.All(sheet => sheet.Id != sheetId)) throw new InvalidDataException("The sheet does not exist.");
        Commit("Transaction.SetSheetGuideGrid", document =>
        {
            var index = document.Sheets.FindIndex(sheet => sheet.Id == sheetId);
            document.Sheets[index] = document.Sheets[index] with { GuideGridMm = spacingMm };
        });
    }

    /// <summary>Places a mark in the plan of a level that points to a view and the sheet that shows it.</summary>
    public Guid AddViewReference(Guid levelId, Point2 position, Guid viewId)
    {
        var view = Document.Views.FirstOrDefault(item => item.Id == viewId) ?? throw new InvalidDataException("The view does not exist.");
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        var label = ViewReferences.Label(Document, view);
        var id = Guid.NewGuid();
        Commit("Transaction.AddViewReference", document => document.StructuralElements.Add(new StructuralElement(
            id, levelId, StructuralElementKind.ViewReference, label, position, position, 0, 0, 0)));
        return id;
    }
}
