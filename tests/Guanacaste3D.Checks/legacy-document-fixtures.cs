using Guanacaste3D.Core;

/// <summary>Builds historical fixtures without carrying newer references into schemas that never supported them.</summary>
internal static class LegacyDocumentFixtures
{
    /// <summary>Retains legacy model content while removing later type and editable-sheet definitions.</summary>
    public static BuildingDocument CreateSchemaOne(BuildingDocument source)
    {
        var document = source.Clone(); document.SchemaVersion = 1; document.WallTypes = []; document.SheetFamilies = [];
        document.Sheets = document.Sheets.Select(sheet => sheet with { TitleBlockFamilyId = null, Viewports = [] }).ToList();
        return document;
    }
}
