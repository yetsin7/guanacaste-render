using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Draws actual mesh-plane intersections rather than displaying an elevation under a section title.</summary>
    private void RenderSectionView(SceneMesh mesh)
    {
        var canvas = SectionCanvas;
        canvas.Children.Clear();
        if (canvas.ActualWidth < 180 || canvas.ActualHeight < 100) return;
        var view = _session.Document.Views.FirstOrDefault(candidate => candidate.Id == _sectionViewId &&
            candidate.Kind == ProjectViewKind.Section) ?? _session.Document.Views.FirstOrDefault(view => view.Kind == ProjectViewKind.Section);
        if (view is null) return;
        if (ViewTabs.Items[5] is TabItem tab) tab.Header = view.SectionMode == SectionDisplayMode.Structural
            ? FormatText("Section.StructuralCaption", view.Name) : view.Name;
        var (minimum, maximum) = mesh.Bounds();
        var cut = view.Cut ?? new SectionCut(new((minimum.X + maximum.X) / 2, minimum.Y - 1),
            new((minimum.X + maximum.X) / 2, maximum.Y + 1));
        var contours = SectionGeometry.Build(mesh, cut);
        if (view.SectionMode == SectionDisplayMode.Structural)
            contours = contours.Where(contour => contour.Kind is SurfaceKind.Wall or SurfaceKind.InteriorWall or
                SurfaceKind.Floor or SurfaceKind.Roof or SurfaceKind.Structure).ToArray();
        var minZ = Math.Min(minimum.Z, _session.Document.Levels.Min(level => level.Elevation)) - 0.5;
        var maxZ = Math.Max(maximum.Z, _session.Document.Levels.Max(level => level.Elevation)) + 0.5;
        var width = cut.Start.DistanceTo(cut.End);
        var scale = Math.Min((canvas.ActualWidth - 190) / Math.Max(1, width),
            (canvas.ActualHeight - 100) / Math.Max(1, maxZ - minZ));
        scale = Math.Max(0.1, scale);
        var left = (canvas.ActualWidth - width * scale) / 2;
        DrawProjectedLevels(canvas, 0, width, minZ, left, scale);
        foreach (var group in contours.GroupBy(contour => contour.Kind))
        {
            var closed = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var context = closed.Open())
            {
                foreach (var contour in group.Where(contour => contour.Closed && contour.Points.Count > 2))
                {
                    context.BeginFigure(new(left + contour.Points[0].X * scale,
                        ProjectElevation(canvas, contour.Points[0].Y, minZ, scale)), true, true);
                    context.PolyLineTo(contour.Points.Skip(1).Select(point => new Point(left + point.X * scale,
                        ProjectElevation(canvas, point.Y, minZ, scale))).ToArray(), true, false);
                }
            }
            closed.Freeze();
            canvas.Children.Add(new Path { Data = closed.GetOutlinedPathGeometry(0.1, ToleranceType.Absolute),
                Stroke = Brushes.DarkSlateGray, StrokeThickness = group.Key == SurfaceKind.Structure ? 1.8 : 1.4,
                Fill = SectionFill(group.Key, view.SectionMode), Tag = view.Id });
            foreach (var contour in group.Where(contour => !contour.Closed))
                canvas.Children.Add(new Polyline { Points = new PointCollection(contour.Points.Select(point =>
                    new Point(left + point.X * scale, ProjectElevation(canvas, point.Y, minZ, scale)))),
                    Stroke = Brushes.DarkSlateGray, StrokeThickness = 1.4 });
        }
    }

    /// <summary>Uses stronger structural poche while retaining distinct glass and coordinated-model fills.</summary>
    private static Brush SectionFill(SurfaceKind kind, SectionDisplayMode mode) => kind switch
    {
        SurfaceKind.Glass => Brushes.AliceBlue,
        SurfaceKind.Structure when mode == SectionDisplayMode.Structural => Brushes.SlateGray,
        SurfaceKind.Wall or SurfaceKind.InteriorWall when mode == SectionDisplayMode.Structural => Brushes.DarkGray,
        SurfaceKind.Floor when mode == SectionDisplayMode.Structural => Brushes.Gray,
        _ => Brushes.LightGray
    };
}
