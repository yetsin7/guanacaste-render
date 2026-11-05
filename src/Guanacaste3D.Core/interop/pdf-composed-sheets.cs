namespace Guanacaste3D.Core.Interop;

public static partial class PdfPlanExporter
{
    /// <summary>Prints the user's saved sheet compositions and title-block families at their actual paper sizes.</summary>
    public static byte[] ExportSheets(BuildingDocument document, DateTimeOffset date)
    {
        document.Validate();
        var pdf = new PdfWriter();
        foreach (var sheet in document.Sheets)
        {
            var composition = SheetComposition.Build(document, sheet, date);
            pdf.AddPage(composition.Width, composition.Height, page => DrawComposition(page, composition));
        }
        return pdf.ToBytes();
    }

    /// <summary>Clips each placed view independently so an exact user scale cannot spill into neighboring views or the footer.</summary>
    private static void DrawComposition(PdfPage page, ComposedSheet sheet)
    {
        foreach (var viewport in sheet.Viewports)
        {
            var bounds = viewport.Placement;
            page.BeginClip(bounds.X, sheet.Height - bounds.Y - bounds.Height + 14, bounds.Width, bounds.Height - 14);
            foreach (var graphic in viewport.Graphics) DrawPaperPrimitive(page, graphic, sheet.Height);
            page.EndClip();
        }
        foreach (var graphic in sheet.Graphics) DrawPaperPrimitive(page, graphic, sheet.Height);
    }

    /// <summary>Maps the shared top-left paper coordinate system into PDF's bottom-left coordinate system.</summary>
    private static void DrawPaperPrimitive(PdfPage page, SheetPrimitive primitive, double height)
    {
        /// <summary>Converts paper-space Y to the PDF baseline axis.</summary>
        (double, double) Map(Point2 point) => (point.X, height - point.Y);
        switch (primitive)
        {
            case SheetPolygon polygon: page.Path(polygon.Points.Select(Map).ToArray(), true, polygon.Weight, polygon.Color, polygon.Fill); break;
            case SheetPolyline line: page.Path(line.Points.Select(Map).ToArray(), false, line.Weight, line.Color, null, line.Dashed); break;
            case SheetText text: page.Text(text.Position.X, height - text.Position.Y - text.Size * 0.85, text.Text, text.Size, text.Color, text.Bold); break;
        }
    }
}
