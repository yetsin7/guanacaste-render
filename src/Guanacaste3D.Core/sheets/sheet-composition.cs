using System.Globalization;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Interop;

namespace Guanacaste3D.Core;

/// <summary>A bounded placed drawing in paper millimeters, with a separate view caption and scale.</summary>
public sealed record ComposedViewport(SheetViewport Placement, double Scale, IReadOnlyList<SheetPrimitive> Graphics);

/// <summary>One shared paper-space representation consumed by both the desktop preview and vector PDF output.</summary>
public sealed record ComposedSheet(double Width, double Height, IReadOnlyList<SheetPrimitive> Graphics,
    IReadOnlyList<ComposedViewport> Viewports);

/// <summary>Coordinates reusable family graphics, sheet fields, and independently scaled views.</summary>
public static class SheetComposition
{
    private static readonly (double, double, double) Ink = (0, 0, 0);

    /// <summary>Regenerates a sheet from live model views and the editable native family definition.</summary>
    public static ComposedSheet Build(BuildingDocument document, DrawingSheet sheet, DateTimeOffset date)
    {
        var family = document.SheetFamilies.Single(family => family.Id == sheet.TitleBlockFamilyId);
        var layout = family.SheetLayout!;
        var graphics = new List<SheetPrimitive>();
        var fields = new Dictionary<string, string>
        {
            ["ProjectName"] = document.Name, ["SheetName"] = sheet.Name, ["SheetNumber"] = sheet.Number,
            ["Description"] = sheet.Description, ["Subtitle"] = sheet.Subtitle,
            ["Date"] = sheet.DateText.Length > 0 ? sheet.DateText : date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture)
        };
        foreach (var graphic in layout.Graphics)
        {
            if (graphic.Kind == SheetGraphicKind.Text)
            {
                var text = graphic.Text;
                foreach (var field in fields) text = text.Replace("{" + field.Key + "}", field.Value, StringComparison.Ordinal);
                graphics.Add(new SheetText(graphic.Start, text, graphic.FontSize, Ink, graphic.Bold));
            }
            else if (graphic.Kind == SheetGraphicKind.Rectangle)
                graphics.Add(new SheetPolygon([graphic.Start, new(graphic.End.X, graphic.Start.Y), graphic.End,
                    new(graphic.Start.X, graphic.End.Y)], graphic.Weight, Ink, null));
            else graphics.Add(new SheetPolyline([graphic.Start, graphic.End], graphic.Weight, Ink));
        }
        var mesh = sheet.Viewports.Any(viewport => document.Views.Any(view => view.Id == viewport.ViewId &&
            view.Kind != ProjectViewKind.Plan && view.Id != BuiltInProjectViews.AreaScheduleId && view.Id != BuiltInProjectViews.LocationId)) ? BuildingMesher.Build(document) : null;
        var viewports = new List<ComposedViewport>();
        foreach (var placement in sheet.Viewports)
        {
            if (document.Schedules.FirstOrDefault(schedule => schedule.Id == placement.ViewId) is { } schedule)
            {
                viewports.Add(new(placement, 1, ScheduleSheetTable.Primitives(document, schedule, placement)));
                continue;
            }
            var view = document.Views.Single(view => view.Id == placement.ViewId);
            if (view.Id == BuiltInProjectViews.AreaScheduleId)
            {
                // A table is drawn straight in paper millimeters: it has no model scale to fit.
                viewports.Add(new(placement, 1, AreaSchedule.Primitives(document, placement)));
                var tableY = placement.Y + placement.Height - 10;
                graphics.Add(new SheetPolyline([new(placement.X + 8, tableY + 4), new(placement.X + placement.Width, tableY + 4)], 0.25, Ink));
                graphics.Add(new SheetText(new(placement.X + 2, tableY + 0.5), placement.Number, 2.5, Ink));
                graphics.Add(new SheetText(new(placement.X + 8, tableY), view.Name, 3, Ink, true));
                continue;
            }
            var source = ViewGraphics(document, view, mesh);
            if (view.ModelCrop is { } crop) source = SheetPrimitiveClipper.Clip(source, crop);
            var points = source.SelectMany(Vertices).ToArray();
            var minX = points.Select(point => point.X).DefaultIfEmpty(0).Min();
            var minY = points.Select(point => point.Y).DefaultIfEmpty(0).Min();
            var maxX = points.Select(point => point.X).DefaultIfEmpty(1).Max();
            var maxY = points.Select(point => point.Y).DefaultIfEmpty(1).Max();
            var scale = placement.Scale > 0 ? placement.Scale : PdfPlanExporter.ChooseScale(Math.Max(1, maxX - minX),
                Math.Max(1, maxY - minY), placement.Width - 8, placement.Height - 18);
            var factor = 1000 / scale;
            var centerX = placement.X + placement.Width / 2;
            var centerY = placement.Y + (placement.Height - 14) / 2;
            /// <summary>Places metric view coordinates inside the requested paper rectangle without changing its scale.</summary>
            Point2 Map(Point2 point) => new(centerX + (point.X - (minX + maxX) / 2) * factor,
                centerY - (point.Y - (minY + maxY) / 2) * factor);
            var mapped = source.Select(primitive => MapPrimitive(primitive, Map)).ToArray();
            viewports.Add(new(placement, scale, mapped));
            var captionY = placement.Y + placement.Height - 10;
            graphics.Add(new SheetPolygon(Enumerable.Range(0, 32).Select(index => new Point2(placement.X + 3 +
                3 * Math.Cos(index * Math.PI / 16), captionY + 2 + 3 * Math.Sin(index * Math.PI / 16))).ToArray(), 0.2, Ink, null));
            graphics.Add(new SheetPolyline([new(placement.X + 8, captionY + 4),
                new(placement.X + placement.Width, captionY + 4)], 0.25, Ink));
            graphics.Add(new SheetText(new(placement.X + 2, captionY + 0.5), placement.Number, 2.5, Ink));
            graphics.Add(new SheetText(new(placement.X + 8, captionY), view.Name, 3, Ink, true));
            graphics.Add(new SheetText(new(placement.X + 8, captionY + 5),
                $"1 : {scale.ToString("0.##", CultureInfo.CurrentCulture)}", 2.5, Ink));
        }
        return new(layout.Width, layout.Height, graphics, viewports);
    }

    /// <summary>Reuses the same plan/cut geometry and adds a lightweight isometric line view for cover sheets.</summary>
    private static IReadOnlyList<SheetPrimitive> ViewGraphics(BuildingDocument document, ProjectView view, SceneMesh? mesh)
    {
        if (view.Id == BuiltInProjectViews.LocationId) return LocationPlan.Primitives(document);
        if (view.Kind == ProjectViewKind.Plan) return PdfPlanExporter.PlanPrimitives(document, view.LevelId!.Value);
        if (view.Kind != ProjectViewKind.Model3D) return PdfPlanExporter.GeneratedViewPrimitives(document, view, mesh);
        mesh ??= BuildingMesher.Build(document);
        /// <summary>Projects model coordinates onto fixed orthographic isometric drawing axes.</summary>
        Point2 Project(System.Numerics.Vector3 point) => new((point.X - point.Y) * 0.70710678,
            (point.X + point.Y) * 0.40824829 + point.Z * 0.81649658);
        return FeatureEdges.Extract(mesh.Positions, mesh.Indices).Select(edge =>
            (SheetPrimitive)new SheetPolyline([Project(edge.A), Project(edge.B)], 0.13, Ink)).ToArray();
    }

    /// <summary>Supplies model-space extents for fitting, including annotation anchors.</summary>
    private static IEnumerable<Point2> Vertices(SheetPrimitive primitive) => primitive switch
    {
        SheetPolygon polygon => polygon.Points, SheetPolyline line => line.Points,
        SheetText text => [text.Position], _ => []
    };

    /// <summary>Transforms model coordinates while keeping text sizes and line weights in physical paper units.</summary>
    private static SheetPrimitive MapPrimitive(SheetPrimitive primitive, Func<Point2, Point2> map) => primitive switch
    {
        SheetPolygon polygon => MapPolygon(polygon, map),
        SheetPolyline line => line with { Points = line.Points.Select(map).ToArray() },
        SheetText text => text with { Position = new(map(text.Position).X - text.Text.Length * text.Size * 0.25,
            map(text.Position).Y - text.Size * (text.CenteredOnSymbol ? 0.5 : 0.85)), CenteredOnSymbol = false }, _ => primitive
    };

    /// <summary>Transforms solid geometry normally while retaining physical annotation-head sizes at every viewport scale.</summary>
    internal static SheetPolygon MapPolygon(SheetPolygon polygon, Func<Point2, Point2> map)
    {
        var points = polygon.Points.Select(map).ToArray();
        if (polygon.PaperRadius is double radius && points.Length > 0)
        {
            var center = new Point2(points.Average(point => point.X), points.Average(point => point.Y));
            points = points.Select(point => { var length = Math.Max(1e-9, point.DistanceTo(center));
                return new Point2(center.X + (point.X - center.X) / length * radius,
                    center.Y + (point.Y - center.Y) / length * radius); }).ToArray();
        }
        return polygon with { Points = points, PaperRadius = null };
    }
}
