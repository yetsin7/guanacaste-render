using System.Globalization;
using System.Text;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Interop;

/// <summary>A vector drawing primitive of a plan sheet, in model meters (or sheet millimeters for the title block).</summary>
public abstract record SheetPrimitive(double Weight, (double R, double G, double B) Color);

/// <summary>A closed outline, optionally filled.</summary>
public sealed record SheetPolygon(IReadOnlyList<Point2> Points, double Weight, (double R, double G, double B) Color,
    (double R, double G, double B)? Fill) : SheetPrimitive(Weight, Color)
{
    /// <summary>Optional annotation radius in paper millimeters, independent of the model/view scale.</summary>
    public double? PaperRadius { get; init; }
}

/// <summary>An open polyline.</summary>
public sealed record SheetPolyline(IReadOnlyList<Point2> Points, double Weight, (double R, double G, double B) Color,
    bool Dashed = false) : SheetPrimitive(Weight, Color);

/// <summary>A line of text anchored at its lower left corner; size in sheet millimeters.</summary>
public sealed record SheetText(Point2 Position, string Text, double Size, (double R, double G, double B) Color,
    bool Bold = false) : SheetPrimitive(0, Color)
{
    /// <summary>Centers the label inside an annotation head instead of treating the anchor as a baseline.</summary>
    public bool CenteredOnSymbol { get; init; }
}

/// <summary>
/// Exports the floor plans of a project as a vector PDF, one sheet per level, the way Revit prints plan views on
/// sheets: walls cut and filled dark, doors with their leaves and swings, windows, rooms with name and area, floors,
/// stairs with their walking line, columns, structure, plantings and terrain contours. Each sheet carries a title block,
/// a scale bar and a north arrow, and the plan is drawn at the largest standard scale that fits the paper.
/// </summary>
public static partial class PdfPlanExporter
{
    private const double PointsPerMillimeter = 72 / 25.4;
    private static readonly (double, double, double) Black = (0, 0, 0);
    private static readonly (double, double, double) Grey = (0.45, 0.45, 0.45);
    private static readonly (double, double, double) Vegetation = (0.62, 0.66, 0.60);
    private static readonly (double, double, double) Light = (0.65, 0.65, 0.65);
    private static readonly double[] Scales = [20, 25, 50, 75, 100, 125, 150, 200, 250, 500, 1000, 2000];

    /// <summary>Paper size in millimeters, landscape.</summary>
    public static (double Width, double Height) PaperSize(SheetPaperSize paper) => paper switch
    {
        SheetPaperSize.A0 => (1189, 841),
        SheetPaperSize.A1 => (841, 594),
        SheetPaperSize.A2 => (594, 420),
        SheetPaperSize.A4 => (297, 210),
        _ => (420, 297)
    };

    /// <summary>The largest standard scale denominator at which a drawing of the given extent fits the given area.</summary>
    public static double ChooseScale(double drawingWidth, double drawingHeight, double areaWidthMm, double areaHeightMm)
    {
        foreach (var scale in Scales)
            if (drawingWidth * 1000 / scale <= areaWidthMm && drawingHeight * 1000 / scale <= areaHeightMm) return scale;
        return Scales[^1];
    }

    /// <summary>Writes every level's plan as one page of a PDF document.</summary>
    public static byte[] Export(BuildingDocument document, SheetPaperSize paper, DateTimeOffset date, string? sheetPrefix = "A")
    {
        ArgumentNullException.ThrowIfNull(document);
        var pdf = new PdfWriter();
        var (width, height) = PaperSize(paper);
        var number = 1;
        foreach (var level in document.Levels.OrderBy(level => level.Elevation))
        {
            var primitives = PlanPrimitives(document, level.Id);
            pdf.AddPage(width, height, page => DrawSheet(page, document, level, primitives, width, height, date,
                $"{sheetPrefix}-{number++:D2}"));
        }
        return pdf.ToBytes();
    }

    /// <summary>The plan of one level as vector primitives in model meters.</summary>
    public static IReadOnlyList<SheetPrimitive> PlanPrimitives(BuildingDocument document, Guid levelId)
    {
        var items = new List<SheetPrimitive>();
        var joins = WallJoinIndex.Build(document.Walls);
        var types = document.OpeningTypes.ToDictionary(type => type.Id);

        foreach (var floor in document.Floors.Where(floor => floor.LevelId == levelId))
        {
            if (floor.IsToposolid)
            {
                items.Add(new SheetPolygon(floor.Boundary, 0.25, (0.27, 0.45, 0.22), null));
                foreach (var segment in TopographyGeometry.Contours(floor, 0.5))
                {
                    var major = Math.Abs(segment.Elevation / 2.5 - Math.Round(segment.Elevation / 2.5)) < 1e-6;
                    items.Add(new SheetPolyline([segment.Start, segment.End], major ? 0.25 : 0.1, (0.45, 0.35, 0.22)));
                }
                continue;
            }
            var built = RoofGeometry.WithOverhang(floor);
            items.Add(new SheetPolygon(built.Boundary, 0.13, Light, null));
            foreach (var (from, to) in RoofGeometry.CreaseLines(built))
                items.Add(new SheetPolyline([from, to], 0.13, Light, Dashed: true));
        }

        foreach (var room in document.Rooms.Where(room => room.LevelId == levelId))
        {
            var center = new Point2((room.FirstCorner.X + room.SecondCorner.X) / 2, (room.FirstCorner.Y + room.SecondCorner.Y) / 2);
            var area = Math.Abs(room.SecondCorner.X - room.FirstCorner.X) * Math.Abs(room.SecondCorner.Y - room.FirstCorner.Y);
            items.Add(new SheetText(center, room.Name, 3.0, Black, Bold: true));
            items.Add(new SheetText(new Point2(center.X, center.Y - 0.45), $"{area.ToString("0.00", CultureInfo.GetCultureInfo("es-ES"))} m²", 2.2, Grey));
        }

        foreach (var wall in document.Walls.Where(wall => wall.LevelId == levelId))
        {
            var openings = document.Openings.Where(opening => opening.WallId == wall.Id).ToArray();
            foreach (var solid in WallGeometry.Solids(wall, openings).Where(solid => solid.Bottom <= 1.2 && solid.Top >= 1.2))
                items.Add(new SheetPolygon(joins.SectionOutline(wall, solid.From, solid.To), 0.5, Black, (0.15, 0.15, 0.15)));
            foreach (var opening in openings) AddOpening(items, wall, opening, types.GetValueOrDefault(opening.OpeningTypeId));
        }

        foreach (var column in document.Columns.Where(column => column.LevelId == levelId))
            items.Add(new SheetPolygon(column.Outline, 0.5, Black, (0.15, 0.15, 0.15)));

        foreach (var element in document.StructuralElements.Where(element => element.LevelId == levelId))
        {
            if (element.Kind is StructuralElementKind.Opening or StructuralElementKind.Shaft)
            {
                items.Add(new SheetPolygon(element.Footprint, 0.25, Black, null));
                continue;
            }
            if (element.Kind == StructuralElementKind.Planting)
            {
                items.Add(new SheetPolygon(Circle(element.Start, element.Width / 2, 48), 0.18, (0.27, 0.45, 0.22), null));
                items.Add(new SheetPolygon(Circle(element.Start, Math.Max(0.05, element.Width * 0.03), 16), 0.18, (0.27, 0.45, 0.22), null));
                continue;
            }
            if (element.IsDatumLine || !element.HasSolid) continue;
            foreach (var part in ElementGeometry.Parts(element))
                items.Add(new SheetPolygon(part.Footprint, 0.18, Grey, null));
            if (element.Kind == StructuralElementKind.Stair)
            {
                var walk = StairGeometry.WalkLine(element);
                items.Add(new SheetPolyline(walk, 0.18, Black));
                if (walk.Count >= 2) items.Add(new SheetPolyline(Arrow(walk[^2], walk[^1], Math.Min(0.3, element.Width / 3)), 0.18, Black));
            }
        }
        AddDocumentationPrimitives(items, document, levelId);
        return items;
    }

    private static void AddOpening(List<SheetPrimitive> items, Wall wall, WallOpening opening, OpeningType? type)
    {
        var length = Math.Max(wall.Length, 1e-9);
        var dx = (wall.End.X - wall.Start.X) / length;
        var dy = (wall.End.Y - wall.Start.Y) / length;
        Point2 At(double u, double v) => new(wall.Start.X + dx * u - dy * v, wall.Start.Y + dy * u + dx * v);
        var half = wall.Thickness / 2;
        if (opening.Kind == OpeningKind.Door)
        {
            var symbol = DoorPlanSymbol.Build(opening.Begin, opening.End, wall.Thickness,
                type is null ? 0.04 : WallGeometry.LeafDepth(type, wall), type?.Operation ?? OpeningOperation.Standard,
                opening.FlipHand, opening.FlipFacing);
            foreach (var outline in symbol.Outlines)
                items.Add(new SheetPolygon(outline.Points.Select(point => At(point.U, point.V)).ToArray(), 0.18, Black, null));
            foreach (var arc in symbol.Swings)
                items.Add(new SheetPolyline(DoorPlanSymbol.Sample(arc).Select(point => At(point.U, point.V)).ToArray(), 0.1, Black));
            foreach (var line in symbol.Thresholds)
                items.Add(new SheetPolyline([At(line.From.U, line.From.V), At(line.To.U, line.To.V)], 0.1, Black));
            return;
        }
        // A window: jamb lines, the two wall faces and a double line of glass in the middle.
        foreach (var v in new[] { -half, half })
            items.Add(new SheetPolyline([At(opening.Begin, v), At(opening.End, v)], 0.18, Black));
        foreach (var v in new[] { -0.015, 0.015 })
            items.Add(new SheetPolyline([At(opening.Begin, v), At(opening.End, v)], 0.1, Black));
        foreach (var u in new[] { opening.Begin, opening.End })
            items.Add(new SheetPolyline([At(u, -half), At(u, half)], 0.25, Black));
    }

    /// <summary>Fits vector model geometry above a title block; generated views omit the plan north marker.</summary>
    private static void DrawSheet(PdfPage page, BuildingDocument document, Level level, IReadOnlyList<SheetPrimitive> primitives,
        double width, double height, DateTimeOffset date, string number, string? viewTitle = null)
    {
        const double margin = 10;
        const double titleHeight = 32;
        // Border.
        page.Rectangle(margin, margin, width - 2 * margin, height - 2 * margin, 0.5, Black);
        // Drawing area above the title strip.
        var area = (X: margin + 8, Y: margin + titleHeight + 8, W: width - 2 * margin - 16, H: height - 2 * margin - titleHeight - 16);
        var points = primitives.SelectMany(primitive => primitive switch
        {
            SheetPolygon polygon => polygon.Points,
            SheetPolyline line => line.Points,
            SheetText text => [text.Position],
            _ => []
        }).ToArray();
        var scale = 100d;
        if (points.Length > 0)
        {
            var minX = points.Min(point => point.X);
            var maxX = points.Max(point => point.X);
            var minY = points.Min(point => point.Y);
            var maxY = points.Max(point => point.Y);
            scale = ChooseScale(Math.Max(1, maxX - minX), Math.Max(1, maxY - minY), area.W, area.H);
            var factor = 1000 / scale;
            var offsetX = area.X + area.W / 2 - (minX + maxX) / 2 * factor;
            var offsetY = area.Y + area.H / 2 - (minY + maxY) / 2 * factor;
            (double, double) Map(Point2 point) => (offsetX + point.X * factor, offsetY + point.Y * factor);
            foreach (var primitive in primitives)
            {
                switch (primitive)
                {
                    case SheetPolygon polygon:
                        var paperPolygon = SheetComposition.MapPolygon(polygon, point => { var mapped = Map(point); return new(mapped.Item1, mapped.Item2); });
                        page.Path(paperPolygon.Points.Select(point => (point.X, point.Y)).ToArray(), closed: true, polygon.Weight, polygon.Color, polygon.Fill);
                        break;
                    case SheetPolyline line:
                        page.Path(line.Points.Select(Map).ToArray(), closed: false, line.Weight, line.Color, null, line.Dashed);
                        break;
                    case SheetText text:
                        var (tx, ty) = Map(text.Position);
                        page.Text(tx - text.Text.Length * text.Size * 0.25, ty - (text.CenteredOnSymbol ? text.Size * 0.35 : 0),
                            text.Text, text.Size, text.Color, text.Bold);
                        break;
                }
            }
        }
        DrawTitleBlock(page, document, level, width, margin, titleHeight, scale, date, number, viewTitle);
        DrawScaleBar(page, margin + 12, margin + titleHeight + 12, scale);
        if (viewTitle is null) DrawNorthArrow(page, width - margin - 22, height - margin - 26);
    }

    /// <summary>Uses the saved view name when printing a section or elevation.</summary>
    private static void DrawTitleBlock(PdfPage page, BuildingDocument document, Level level, double width, double margin,
        double titleHeight, double scale, DateTimeOffset date, string number, string? viewTitle)
    {
        page.Rectangle(margin, margin, width - 2 * margin, titleHeight, 0.5, Black);
        var right = width - margin;
        var cells = new[] { right - 200, right - 140, right - 80 };
        foreach (var x in cells) page.Path([(x, margin), (x, margin + titleHeight)], false, 0.25, Black, null);
        page.Text(margin + 6, margin + titleHeight - 12, document.Name, 7, Black, true);
        page.Text(margin + 6, margin + titleHeight - 22, viewTitle ?? $"Planta arquitectónica — {level.Name}", 4.5, Black);
        page.Text(margin + 6, margin + 5, "Guanacaste 3D", 3, Grey);
        page.Text(cells[0] + 4, margin + titleHeight - 9, "Escala", 2.5, Grey);
        page.Text(cells[0] + 4, margin + 10, $"1:{scale:0}", 6, Black, true);
        page.Text(cells[1] + 4, margin + titleHeight - 9, "Fecha", 2.5, Grey);
        page.Text(cells[1] + 4, margin + 10, date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), 4.5, Black);
        page.Text(cells[2] + 4, margin + titleHeight - 9, "Plano", 2.5, Grey);
        page.Text(cells[2] + 4, margin + 9, number, 9, Black, true);
    }

    private static void DrawScaleBar(PdfPage page, double x, double y, double scale)
    {
        // Five meters in alternating black and white meter blocks.
        var meter = 1000 / scale;
        for (var index = 0; index < 5; index++)
        {
            var fill = index % 2 == 0 ? Black : (1.0, 1.0, 1.0);
            page.Path([(x + index * meter, y), (x + (index + 1) * meter, y), (x + (index + 1) * meter, y + 2), (x + index * meter, y + 2)],
                true, 0.2, Black, fill);
        }
        page.Text(x, y + 3.5, "0", 2.5, Black);
        page.Text(x + 5 * meter - 1.5, y + 3.5, "5 m", 2.5, Black);
    }

    private static void DrawNorthArrow(PdfPage page, double x, double y)
    {
        page.Path([(x, y + 12), (x - 4, y), (x, y + 3), (x + 4, y)], true, 0.3, Black, Black);
        page.Text(x - 1.5, y + 14, "N", 4, Black, true);
    }

    private static Point2[] Circle(Point2 center, double radius, int segments) =>
        Enumerable.Range(0, segments).Select(index => new Point2(
            center.X + radius * Math.Cos(index * 2 * Math.PI / segments),
            center.Y + radius * Math.Sin(index * 2 * Math.PI / segments))).ToArray();

    private static Point2[] Arrow(Point2 from, Point2 tip, double size)
    {
        var length = Math.Max(from.DistanceTo(tip), 1e-9);
        var dx = (tip.X - from.X) / length;
        var dy = (tip.Y - from.Y) / length;
        return [new(tip.X - dx * size + dy * size * 0.5, tip.Y - dy * size - dx * size * 0.5), tip,
            new(tip.X - dx * size - dy * size * 0.5, tip.Y - dy * size + dx * size * 0.5)];
    }

    /// <summary>One page being drawn, in millimeters from the lower left corner.</summary>
    internal sealed class PdfPage
    {
        private readonly StringBuilder _content = new();

        public string Content => _content.ToString();

        /// <summary>Starts a paper-space viewport clip, retaining the surrounding drawing state.</summary>
        public void BeginClip(double x, double y, double width, double height) =>
            Append($"q {Number(x * PointsPerMillimeter)} {Number(y * PointsPerMillimeter)} {Number(width * PointsPerMillimeter)} {Number(height * PointsPerMillimeter)} re W n ");

        /// <summary>Restores drawing state after an independently clipped viewport.</summary>
        public void EndClip() => Append("Q ");

        public void Rectangle(double x, double y, double width, double height, double weight, (double R, double G, double B) color) =>
            Path([(x, y), (x + width, y), (x + width, y + height), (x, y + height)], true, weight, color, null);

        public void Path(IReadOnlyList<(double X, double Y)> points, bool closed, double weight,
            (double R, double G, double B) color, (double R, double G, double B)? fill, bool dashed = false)
        {
            if (points.Count < 2) return;
            Append($"{Number(weight * PointsPerMillimeter)} w {Number(color.R)} {Number(color.G)} {Number(color.B)} RG ");
            Append(dashed ? $"[{Number(2 * PointsPerMillimeter)} {Number(1.2 * PointsPerMillimeter)}] 0 d " : "[] 0 d ");
            if (fill is { } f) Append($"{Number(f.R)} {Number(f.G)} {Number(f.B)} rg ");
            Append($"{Number(points[0].X * PointsPerMillimeter)} {Number(points[0].Y * PointsPerMillimeter)} m ");
            for (var index = 1; index < points.Count; index++)
                Append($"{Number(points[index].X * PointsPerMillimeter)} {Number(points[index].Y * PointsPerMillimeter)} l ");
            _content.AppendLine(closed ? (fill is null ? "h S" : "h B") : "S");
        }

        public void Text(double x, double y, string text, double size, (double R, double G, double B) color, bool bold = false)
        {
            var escaped = new StringBuilder();
            foreach (var character in text)
            {
                if (character is '(' or ')' or '\\') escaped.Append('\\').Append(character);
                else if (character == '—') escaped.Append('-');
                else if (character < 256) escaped.Append(character);
                else escaped.Append('?');
            }
            _content.AppendLine($"BT /{(bold ? "F2" : "F1")} {Number(size * PointsPerMillimeter)} Tf {Number(color.R)} {Number(color.G)} {Number(color.B)} rg " +
                                $"{Number(x * PointsPerMillimeter)} {Number(y * PointsPerMillimeter)} Td ({escaped}) Tj ET");
        }

        private void Append(string text) => _content.Append(text);

        private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>A minimal PDF 1.4 writer: pages of vector content with the two standard Helvetica fonts.</summary>
    internal sealed class PdfWriter
    {
        private readonly List<(double Width, double Height, string Content)> _pages = [];

        public void AddPage(double widthMm, double heightMm, Action<PdfPage> draw)
        {
            var page = new PdfPage();
            draw(page);
            _pages.Add((widthMm, heightMm, page.Content));
        }

        public byte[] ToBytes()
        {
            // Latin-1 matches WinAnsiEncoding for the accented letters of Spanish text.
            var encoding = Encoding.Latin1;
            var objects = new List<string>();
            var pageIds = new List<int>();
            // 1: catalog, 2: pages, 3 and 4: fonts; each page adds a page object and a content stream.
            var nextId = 5;
            var bodies = new Dictionary<int, string>();
            foreach (var (width, height, content) in _pages)
            {
                var pageId = nextId++;
                var contentId = nextId++;
                pageIds.Add(pageId);
                bodies[pageId] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Pt(width)} {Pt(height)}] " +
                                 $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>";
                bodies[contentId] = $"<< /Length {encoding.GetByteCount(content)} >>\nstream\n{content}endstream";
            }
            bodies[1] = "<< /Type /Catalog /Pages 2 0 R >>";
            bodies[2] = $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>";
            bodies[3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";
            bodies[4] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>";

            using var stream = new MemoryStream();
            void Write(string text) { var bytes = encoding.GetBytes(text); stream.Write(bytes, 0, bytes.Length); }
            Write("%PDF-1.4\n%âãÏÓ\n");
            var offsets = new long[nextId];
            for (var id = 1; id < nextId; id++)
            {
                offsets[id] = stream.Position;
                Write($"{id} 0 obj\n{bodies[id]}\nendobj\n");
            }
            var xref = stream.Position;
            var table = new StringBuilder($"xref\n0 {nextId}\n0000000000 65535 f \n");
            for (var id = 1; id < nextId; id++) table.Append($"{offsets[id]:D10} 00000 n \n");
            Write(table.ToString());
            Write($"trailer\n<< /Size {nextId} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return stream.ToArray();
        }

        private static string Pt(double millimeters) => (millimeters * PointsPerMillimeter).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
