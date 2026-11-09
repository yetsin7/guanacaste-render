namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>Loads an independent native title-block family and creates its first sheet in one transaction.</summary>
    public Guid AddSheetFromFamily(FamilyDocument family)
    {
        if (family.Category != FamilyCategory.TitleBlocks || family.SheetLayout is null)
            throw new InvalidDataException("The family is not a drawing sheet family.");
        family.Validate();
        var id = Guid.NewGuid();
        Commit("Transaction.AddSheet", document =>
        {
            var existing = document.SheetFamilies.FindIndex(candidate => candidate.Id == family.Id);
            if (existing < 0) document.SheetFamilies.Add(family.Clone());
            else document.SheetFamilies[existing] = family.Clone();
            var number = document.Sheets.Count + 1;
            while (document.Sheets.Any(sheet => sheet.Number == $"A{number:D3}")) number++;
            var layout = family.SheetLayout;
            var inset = layout.Margin + 8;
            var plan = document.Views.First(view => view.Kind == ProjectViewKind.Plan);
            document.Sheets.Add(new DrawingSheet(id, $"A{number:D3}", DefaultLabels.Get("SheetName", number),
                SheetFamilies.PaperSizeOf(layout) ?? SheetPaperSize.A1)
            {
                TitleBlockFamilyId = family.Id,
                Viewports = [new(Guid.NewGuid(), plan.Id, inset, inset + layout.HeaderHeight,
                    layout.Width - 2 * inset, layout.Height - 2 * inset - layout.HeaderHeight - layout.FooterHeight)]
            });
        });
        return id;
    }

    /// <summary>Updates one composition and optionally its shared family in a single validated undo step.</summary>
    public void UpdateSheet(DrawingSheet sheet, FamilyDocument? family = null)
    {
        if (!Document.Sheets.Any(candidate => candidate.Id == sheet.Id))
            throw new InvalidDataException("The drawing sheet does not exist.");
        Commit("Transaction.EditSheet", document =>
        {
            if (family is not null)
            {
                if (family.Category != FamilyCategory.TitleBlocks || family.SheetLayout is null)
                    throw new InvalidDataException("The family is not a drawing sheet family.");
                var index = document.SheetFamilies.FindIndex(candidate => candidate.Id == family.Id);
                if (index < 0) document.SheetFamilies.Add(family.Clone());
                else document.SheetFamilies[index] = family.Clone();
            }
            document.Sheets[document.Sheets.FindIndex(candidate => candidate.Id == sheet.Id)] = sheet with { Viewports = [.. sheet.Viewports] };
        });
    }

    /// <summary>Checks reusable family references and independently scaled view placements before saving or regenerating sheets.</summary>
    internal static void ValidateSheetLayouts(BuildingDocument document)
    {
        if (document.SheetFamilies is null || document.SheetFamilies.Count > 1000 ||
            document.SheetFamilies.Select(family => family?.Id).Distinct().Count() != document.SheetFamilies.Count)
            throw new InvalidDataException("Invalid sheet family collection.");
        foreach (var family in document.SheetFamilies)
        {
            if (family is null || family.Category != FamilyCategory.TitleBlocks || family.SheetLayout is null)
                throw new InvalidDataException("The family is not a drawing sheet family.");
            family.Validate();
        }
        var ids = new HashSet<Guid>();
        foreach (var sheet in document.Sheets)
        {
            var family = document.SheetFamilies.FirstOrDefault(family => family.Id == sheet.TitleBlockFamilyId);
            if (family?.SheetLayout is not { } layout || sheet.Viewports is null || sheet.Viewports.Count > 100 ||
                sheet.Description is null || sheet.Description.Length > 1000 || sheet.Subtitle is null || sheet.Subtitle.Length > 1000 ||
                sheet.DateText is null || sheet.DateText.Length > 100)
                throw new InvalidDataException("Invalid drawing sheet layout.");
            foreach (var viewport in sheet.Viewports)
            {
                if (viewport is null || viewport.Id == Guid.Empty || !ids.Add(viewport.Id) ||
                    (!document.Views.Any(view => view.Id == viewport.ViewId && (view.Kind is ProjectViewKind.Plan or
                        ProjectViewKind.Elevation or ProjectViewKind.Section or ProjectViewKind.Model3D ||
                        BuiltInProjectViews.IsGeneratedSheetView(view))) &&
                     !(document.Schedules ?? []).Any(schedule => schedule.Id == viewport.ViewId)) ||
                    !double.IsFinite(viewport.X) || !double.IsFinite(viewport.Y) || !double.IsFinite(viewport.Width) ||
                    !double.IsFinite(viewport.Height) || viewport.X < layout.Margin || viewport.Y < layout.Margin + layout.HeaderHeight ||
                    viewport.Width < 20 || viewport.Height < 20 || viewport.X + viewport.Width > layout.Width - layout.Margin ||
                    viewport.Y + viewport.Height > layout.Height - layout.Margin - layout.FooterHeight ||
                    !double.IsFinite(viewport.Scale) || viewport.Scale is < 0 or > 5000 || (viewport.Scale > 0 && viewport.Scale < 1) ||
                    string.IsNullOrWhiteSpace(viewport.Number) || viewport.Number.Length > 16)
                    throw new InvalidDataException("Invalid sheet viewport placement.");
            }
        }
    }
}
