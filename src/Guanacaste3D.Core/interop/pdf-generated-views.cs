using System.Globalization;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Interop;

public static partial class PdfPlanExporter
{
    /// <summary>Prints coordinated plans, saved sections, and elevation linework in one vector document.</summary>
    public static byte[] ExportDocumentation(BuildingDocument document, SheetPaperSize paper, DateTimeOffset date)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        if (document.Sheets.Count > 0) return ExportSheets(document, date);
        var pdf = new PdfWriter();
        var (width, height) = PaperSize(paper);
        var number = 1;
        foreach (var level in document.Levels.OrderBy(level => level.Elevation))
            pdf.AddPage(width, height, page => DrawSheet(page, document, level, PlanPrimitives(document, level.Id),
                width, height, date, $"A-{number++:D2}"));
        var mesh = BuildingMesher.Build(document);
        foreach (var view in document.Views.Where(view => view.Kind is ProjectViewKind.Section or ProjectViewKind.Elevation))
        {
            var primitives = GeneratedViewPrimitives(document, view, mesh);
            pdf.AddPage(width, height, page => DrawSheet(page, document, document.Levels[0], primitives,
                width, height, date, $"A-{number++:D2}", view.Name));
        }
        return pdf.ToBytes();
    }

    /// <summary>Returns actual cut profiles or projected feature edges, plus their coordinated level datums.</summary>
    public static IReadOnlyList<SheetPrimitive> GeneratedViewPrimitives(BuildingDocument document, ProjectView view, SceneMesh? mesh = null)
    {
        if (view.Kind is not (ProjectViewKind.Section or ProjectViewKind.Elevation))
            throw new ArgumentException("Only section and elevation views have generated documentation geometry.", nameof(view));
        mesh ??= BuildingMesher.Build(document);
        var items = new List<SheetPrimitive>();
        var (minimum, maximum) = mesh.Bounds();
        double minX;
        double maxX;
        if (view.Kind == ProjectViewKind.Section)
        {
            var cut = view.Cut ?? new SectionCut(new((minimum.X + maximum.X) / 2, minimum.Y - 1),
                new((minimum.X + maximum.X) / 2, maximum.Y + 1));
            minX = 0;
            maxX = cut.Start.DistanceTo(cut.End);
            foreach (var contour in SectionGeometry.Build(mesh, cut))
                items.Add(new SheetPolyline(contour.Points, contour.Kind == SurfaceKind.Glass ? 0.13 : 0.35, Black));
        }
        else
        {
            var projected = mesh.Positions.Select(vertex => ProjectHorizontal(new(vertex.X, vertex.Y), view.Orientation)).ToArray();
            minX = projected.DefaultIfEmpty(-5).Min();
            maxX = projected.DefaultIfEmpty(5).Max();
            // Vegetation draws in thin light grey, as entourage does on architectural elevations, so a tree in front of
            // the facade never hides it under a mass of black lines.
            var architecture = new List<int>();
            var vegetation = new List<int>();
            for (var triangle = 0; triangle < mesh.TriangleCount; triangle++)
                (mesh.TriangleKinds[triangle] is SurfaceKind.Foliage or SurfaceKind.Leaf or SurfaceKind.Bark ? vegetation : architecture)
                    .AddRange([mesh.Indices[triangle * 3], mesh.Indices[triangle * 3 + 1], mesh.Indices[triangle * 3 + 2]]);
            foreach (var (indices, weight, color) in new[] { (architecture, 0.13, Black), (vegetation, 0.05, Vegetation) })
                foreach (var edge in FeatureEdges.Extract(mesh.Positions, indices))
                    items.Add(new SheetPolyline([new(ProjectHorizontal(new(edge.A.X, edge.A.Y), view.Orientation), edge.A.Z),
                        new(ProjectHorizontal(new(edge.B.X, edge.B.Y), view.Orientation), edge.B.Z)], weight, color));
            foreach (var axis in document.Grids)
            {
                var start = ProjectHorizontal(axis.Start, view.Orientation);
                var end = ProjectHorizontal(axis.End, view.Orientation);
                if (Math.Abs(start - end) > 0.00001) continue;
                items.Add(new SheetPolyline([new(start, minimum.Z - 0.5), new(start, maximum.Z + 0.9)], 0.13, Grey, Dashed: true));
                items.Add(new SheetPolygon(Circle(new(start, maximum.Z + 1.1), 0.2, 32), 0.18, Black, (1, 1, 1)) { PaperRadius = 3 });
                items.Add(new SheetText(new(start, maximum.Z + 1.1), axis.Name, 2.5, Black) { CenteredOnSymbol = true });
                minX = Math.Min(minX, start);
                maxX = Math.Max(maxX, start);
            }
        }
        foreach (var level in document.Levels)
        {
            items.Add(new SheetPolyline([new(minX - 0.5, level.Elevation), new(maxX + 0.5, level.Elevation)], 0.13, Grey, Dashed: true));
            items.Add(new SheetText(new(minX - 0.5, level.Elevation + 0.1),
                $"{level.Name}  {level.Elevation.ToString("0.00", CultureInfo.CurrentCulture)} m", 2.5, Black));
        }
        return items;
    }

    /// <summary>Matches the desktop's cardinal viewing axes, including opposite-face mirroring.</summary>
    private static double ProjectHorizontal(Point2 point, ViewOrientation orientation) => orientation switch
    {
        ViewOrientation.East => point.Y,
        ViewOrientation.West => -point.Y,
        ViewOrientation.South => -point.X,
        _ => point.X
    };
}
