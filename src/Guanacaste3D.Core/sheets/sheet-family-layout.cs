namespace Guanacaste3D.Core;

/// <summary>Editable paper-space graphic roles in a title-block family.</summary>
public enum SheetGraphicKind { Line, Rectangle, Text }

/// <summary>One editable family graphic in millimeters from the paper's top-left corner.</summary>
public sealed record SheetGraphic(Guid Id, SheetGraphicKind Kind, Point2 Start, Point2 End,
    string Text = "", double FontSize = 2.5, double Weight = 0.25, bool Bold = false);

/// <summary>Defines a reusable drawing border, headers, and title-block fields independently of model geometry.</summary>
public sealed record SheetFamilyLayout(double Width, double Height, double Margin, double HeaderHeight,
    double FooterHeight, List<SheetGraphic> Graphics)
{
    /// <summary>Copies the editable graphics collection for transaction and family-file isolation.</summary>
    public SheetFamilyLayout Copy() => this with { Graphics = [.. Graphics] };

    /// <summary>Rejects malformed paper geometry before rendering or sharing a .g3df family.</summary>
    public void Validate()
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width is < 100 or > 2000 || Height is < 100 or > 2000 ||
            !double.IsFinite(Margin) || Margin is < 0 or > 100 || !double.IsFinite(HeaderHeight) || HeaderHeight < 0 ||
            !double.IsFinite(FooterHeight) || FooterHeight < 0 || HeaderHeight + FooterHeight + 2 * Margin >= Height - 20 ||
            2 * Margin >= Width - 20 || Graphics is null || Graphics.Count > 2000 ||
            Graphics.Select(graphic => graphic?.Id).Distinct().Count() != Graphics.Count ||
            Graphics.Any(graphic => graphic is null || graphic.Id == Guid.Empty || !Enum.IsDefined(graphic.Kind) ||
                !ValidPoint(graphic.Start) || !ValidPoint(graphic.End) || graphic.Text is null || graphic.Text.Length > 2000 ||
                !double.IsFinite(graphic.FontSize) || graphic.FontSize is < 0.5 or > 40 ||
                !double.IsFinite(graphic.Weight) || graphic.Weight is < 0.05 or > 5))
            throw new InvalidDataException("Invalid sheet family layout.");
    }

    /// <summary>Restricts editable paper graphics to finite points inside the family's page.</summary>
    private bool ValidPoint(Point2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y) &&
        point.X >= 0 && point.Y >= 0 && point.X <= Width && point.Y <= Height;
}

/// <summary>A placed view with its own paper-space rectangle and exact scale denominator; zero requests fitting.</summary>
public sealed record SheetViewport(Guid Id, Guid ViewId, double X, double Y, double Width, double Height,
    double Scale = 0, string Number = "1");

/// <summary>Builds editable architectural borders following the user's header/drawing/footer reference composition.</summary>
public static class SheetFamilies
{
    /// <summary>Recognizes standard physical paper sizes in either orientation while allowing custom family dimensions.</summary>
    public static SheetPaperSize? PaperSizeOf(SheetFamilyLayout layout)
    {
        foreach (var paper in Enum.GetValues<SheetPaperSize>())
        {
            var size = Interop.PdfPlanExporter.PaperSize(paper);
            if (Math.Abs(Math.Max(layout.Width, layout.Height) - size.Width) < 0.1 &&
                Math.Abs(Math.Min(layout.Width, layout.Height) - size.Height) < 0.1) return paper;
        }
        return null;
    }

    /// <summary>Creates a native family that can be edited, saved as .g3df, and reused by multiple drawing sheets.</summary>
    public static FamilyDocument Create(SheetPaperSize paper, bool portrait = true)
    {
        var (wide, shortSide) = Interop.PdfPlanExporter.PaperSize(paper);
        var width = portrait ? shortSide : wide;
        var height = portrait ? wide : shortSide;
        const double margin = 5;
        var graphics = new List<SheetGraphic>
        {
            new(Guid.NewGuid(), SheetGraphicKind.Rectangle, new(margin, margin), new(width - margin, height - margin)),
            new(Guid.NewGuid(), SheetGraphicKind.Line, new(12, 44), new(width - 12, 44)),
            new(Guid.NewGuid(), SheetGraphicKind.Line, new(12, height - 44), new(width - 12, height - 44)),
            new(Guid.NewGuid(), SheetGraphicKind.Text, new(12, 16), new(12, 16), "{SheetName}", 4, Bold: true),
            new(Guid.NewGuid(), SheetGraphicKind.Text, new(12, 32), new(12, 32), "{Description}", 2.5),
            new(Guid.NewGuid(), SheetGraphicKind.Text, new(12, height - 36), new(12, height - 36), "{ProjectName}", 3),
            new(Guid.NewGuid(), SheetGraphicKind.Text, new(12, height - 22), new(12, height - 22), "{Subtitle}", 2.5),
            new(Guid.NewGuid(), SheetGraphicKind.Text, new(width * 0.64, height - 36), new(width * 0.64, height - 36), "{SheetNumber}", 3),
            new(Guid.NewGuid(), SheetGraphicKind.Text, new(width - 55, height - 36), new(width - 55, height - 36), "{Date}", 2.5)
        };
        var family = new FamilyDocument { Name = DefaultLabels.Get("SheetFamily"), Category = FamilyCategory.TitleBlocks,
            Types = [new(Guid.NewGuid(), paper.ToString(), [])],
            SheetLayout = new(width, height, margin, 40, 40, graphics) };
        family.Validate();
        return family;
    }

    /// <summary>Creates missing family instances and migrates legacy sheets without discarding their identities.</summary>
    public static void Ensure(BuildingDocument document)
    {
        foreach (var sheet in document.Sheets.ToArray())
        {
            if (sheet.TitleBlockFamilyId is not null) continue;
            var family = Create(sheet.PaperSize, portrait: false);
            document.SheetFamilies.Add(family);
            var index = document.Sheets.FindIndex(candidate => candidate.Id == sheet.Id);
            var layout = family.SheetLayout!;
            var view = document.Views.First(view => view.Kind == ProjectViewKind.Plan);
            var inset = layout.Margin + 8;
            document.Sheets[index] = sheet with { TitleBlockFamilyId = family.Id,
                Viewports = [new(Guid.NewGuid(), view.Id, inset, inset + layout.HeaderHeight,
                    layout.Width - 2 * inset, layout.Height - 2 * inset - layout.HeaderHeight - layout.FooterHeight)] };
        }
    }
}
