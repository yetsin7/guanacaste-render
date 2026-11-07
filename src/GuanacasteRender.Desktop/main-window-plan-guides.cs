using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Shows dashed reference lines while a wall sketch or move destination lines up with a corner
/// that is visible on screen. Only the active level is considered, and the element being moved
/// never guides itself.
/// </summary>
public partial class MainWindow
{
    /// <summary>Screen distance, in device-independent pixels, within which the pointer counts as aligned.</summary>
    private const double AlignmentTolerancePixels = 8;

    private static readonly Brush AlignmentGuideBrush = new SolidColorBrush(Color.FromRgb(214, 51, 108));

    private List<PlanAlignmentGuide> _alignmentGuides = [];

    /// <summary>Recomputes the guides for the raw, unsnapped pointer against the corners on screen.</summary>
    private void UpdateAlignmentGuides(Point2 pointer, Guid? movingId)
    {
        var matches = PlanAlignment.Find(pointer, AlignmentCorners(movingId), VisibleWorldBounds(),
            AlignmentTolerancePixels / _scale);
        _alignmentGuides = [.. ChooseAlignmentGuides(pointer, matches)];
    }

    /// <summary>Lists the corners of active-level walls, rooms, and floors, leaving out the element being moved.</summary>
    private IEnumerable<Point2> AlignmentCorners(Guid? movingId)
    {
        foreach (var wall in _session.Document.Walls.Where(candidate =>
                     candidate.LevelId == _activeLevelId && candidate.Id != movingId))
        {
            yield return wall.Start;
            yield return wall.End;
        }
        foreach (var room in _session.Document.Rooms.Where(candidate =>
                     candidate.LevelId == _activeLevelId && candidate.Id != movingId))
        {
            var first = room.FirstCorner;
            var second = room.SecondCorner;
            yield return first;
            yield return new Point2(second.X, first.Y);
            yield return second;
            yield return new Point2(first.X, second.Y);
        }
        foreach (var floor in _session.Document.Floors.Where(candidate =>
                     candidate.LevelId == _activeLevelId && candidate.Id != movingId))
        {
            foreach (var point in floor.Boundary) yield return point;
        }
    }

    /// <summary>
    /// Decides which of the corners the pointer lines up with are shown as guides. The matches arrive
    /// in corner order, one entry per axis, so the choice is made here, not in the lookup.
    /// </summary>
    private static IReadOnlyList<PlanAlignmentGuide> ChooseAlignmentGuides(
        Point2 pointer, IReadOnlyList<PlanAlignmentGuide> matches)
    {
        // One guide per axis, from the corner nearest the pointer, as Revit does: several parallel dashed lines
        // through the same pointer would be noise, and the snap must follow exactly the line that is drawn.
        return matches.GroupBy(guide => guide.Axis)
            .Select(axis => axis.MinBy(guide => guide.Corner.DistanceTo(pointer)))
            .ToArray();
    }

    /// <summary>Returns the model-space rectangle the plan viewport currently shows.</summary>
    private PlanBounds VisibleWorldBounds()
    {
        var topLeft = CanvasToWorld(new Point(0, 0));
        var bottomRight = CanvasToWorld(new Point(PlanCanvas.ActualWidth, PlanCanvas.ActualHeight));
        return new PlanBounds(topLeft.X, bottomRight.Y, bottomRight.X, topLeft.Y);
    }

    /// <summary>Draws every active guide across the visible plan with a marker on the corner it came from.</summary>
    private void AddAlignmentGuides()
    {
        if (_alignmentGuides.Count == 0) return;
        var visible = VisibleWorldBounds();
        foreach (var guide in _alignmentGuides)
        {
            var corner = guide.Corner;
            var from = guide.Axis == PlanGuideAxis.Vertical
                ? new Point2(corner.X, visible.MinY)
                : new Point2(visible.MinX, corner.Y);
            var to = guide.Axis == PlanGuideAxis.Vertical
                ? new Point2(corner.X, visible.MaxY)
                : new Point2(visible.MaxX, corner.Y);
            AddDashedLine(from, to, AlignmentGuideBrush);

            var center = WorldToCanvas(corner);
            var marker = new Ellipse
            {
                Width = 7,
                Height = 7,
                Stroke = AlignmentGuideBrush,
                StrokeThickness = 1.5,
                Fill = Brushes.White,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(marker, center.X - 3.5);
            Canvas.SetTop(marker, center.Y - 3.5);
            PlanCanvas.Children.Add(marker);
        }
    }

    /// <summary>Adds a dashed model-space line to the plan canvas, used for guides and move paths.</summary>
    private void AddDashedLine(Point2 from, Point2 to, Brush brush)
    {
        var start = WorldToCanvas(from);
        var end = WorldToCanvas(to);
        PlanCanvas.Children.Add(new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = brush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false
        });
    }
}
