using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Projects perpendicular building axes as labeled vertical datums through each elevation.</summary>
    private void DrawProjectedAxes(Canvas canvas, ViewOrientation orientation, double minX, double minZ,
        double maxZ, double left, double scale)
    {
        foreach (var axis in _session.Document.Grids)
        {
            var coordinate = ProjectCoordinate(axis.Start, orientation);
            if (Math.Abs(coordinate - ProjectCoordinate(axis.End, orientation)) > 0.00001) continue;
            var x = left + (coordinate - minX) * scale;
            var top = ProjectElevation(canvas, maxZ, minZ, scale);
            canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = ProjectElevation(canvas, minZ, minZ, scale),
                Stroke = Brushes.SlateGray, StrokeThickness = 1, StrokeDashArray = [10, 3, 2, 3], Tag = axis.Id });
            var label = new TextBlock { Text = axis.Name, Foreground = Brushes.SlateGray, Background = Brushes.White, FontSize = 11 };
            Canvas.SetLeft(label, x - 4);
            Canvas.SetTop(label, top - 20);
            canvas.Children.Add(label);
        }
    }

    /// <summary>Includes structural members, foundations, columns, and family edges in coordinated elevations.</summary>
    private void DrawProjectedElements(Canvas canvas, BuildingDocument document, ViewOrientation orientation, double minX, double minZ, double left, double scale)
    {
        foreach (var column in document.Columns)
        {
            var elevation = document.Levels.Single(level => level.Id == column.LevelId).Elevation;
            Point2[] footprint = [new(column.Position.X - column.Width / 2, column.Position.Y - column.Depth / 2),
                new(column.Position.X + column.Width / 2, column.Position.Y + column.Depth / 2)];
            DrawProjectedPart(canvas, new ElementPart(footprint, 0, column.Height, SurfaceKind.Structure),
                elevation, orientation, minX, minZ, left, scale, column.Id);
        }
        foreach (var element in document.StructuralElements.Where(element => element.HasSolid))
        {
            var elevation = document.Levels.Single(level => level.Id == element.LevelId).Elevation;
            foreach (var part in ElementGeometry.Parts(element))
                DrawProjectedPart(canvas, part, elevation, orientation, minX, minZ, left, scale, element.Id);
        }
        foreach (var family in document.InPlaceFamilies)
        {
            var mesh = FamilyGeometry.Tessellate(family);
            foreach (var edge in FeatureEdges.Extract(mesh.Positions, mesh.Indices))
            {
                var a = ProjectCoordinate(new(edge.A.X, edge.A.Y), orientation);
                var b = ProjectCoordinate(new(edge.B.X, edge.B.Y), orientation);
                canvas.Children.Add(new Line { X1 = left + (a - minX) * scale, X2 = left + (b - minX) * scale,
                    Y1 = ProjectElevation(canvas, edge.A.Z, minZ, scale), Y2 = ProjectElevation(canvas, edge.B.Z, minZ, scale),
                    Stroke = Brushes.DimGray, StrokeThickness = 1, Tag = family.Id });
            }
        }
    }

    /// <summary>Projects prism vertices into a convex profile, retaining slopes on ramps and framing.</summary>
    private void DrawProjectedPart(Canvas canvas, ElementPart part, double elevation, ViewOrientation orientation,
        double minX, double minZ, double left, double scale, Guid id)
    {
        var vertices = new List<Point2>();
        for (var index = 0; index < part.Footprint.Count; index++)
        {
            var x = ProjectCoordinate(part.Footprint[index], orientation);
            var end = index is 1 or 2;
            vertices.Add(new(x, elevation + (end ? part.EndBottom ?? part.Bottom : part.Bottom)));
            vertices.Add(new(x, elevation + (end ? part.EndTop ?? part.Top : part.Top)));
        }
        var hull = ProjectionHull(vertices);
        if (hull.Count < 3) return;
        canvas.Children.Add(new Polygon { Points = new PointCollection(hull.Select(point =>
            new Point(left + (point.X - minX) * scale, ProjectElevation(canvas, point.Y, minZ, scale)))),
            Fill = part.Kind == SurfaceKind.Glass ? Brushes.AliceBlue : Brushes.LightGray,
            Stroke = Brushes.DimGray, StrokeThickness = 1, Tag = id });
    }

    /// <summary>Removes interior prism vertices so triangulation does not leave diagonal drafting seams.</summary>
    private static IReadOnlyList<Point2> ProjectionHull(IEnumerable<Point2> vertices)
    {
        var ordered = vertices.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        if (ordered.Length < 3) return ordered;
        var hull = new List<Point2>();
        foreach (var point in ordered)
        {
            while (hull.Count >= 2 && ProjectionTurn(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        var lower = hull.Count;
        foreach (var point in ordered.Reverse().Skip(1))
        {
            while (hull.Count > lower && ProjectionTurn(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    /// <summary>Computes orientation for the monotone-chain silhouette construction.</summary>
    private static double ProjectionTurn(Point2 a, Point2 b, Point2 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}
