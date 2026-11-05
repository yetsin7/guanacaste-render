using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Plan symbols for hosted doors and windows, drawn in the host wall's own coordinates. A door shows
/// its reveal lines, its leaf open at 90° and the swing arc; a window shows its reveal lines and the
/// two glazing lines inside the frame. The same drawing previews an opening while it is being moved.
/// </summary>
public partial class MainWindow
{
    private static readonly SolidColorBrush OpeningFrameBrush = Frozen(Color.FromRgb(61, 71, 79));
    private static readonly SolidColorBrush DoorLeafFill = Frozen(Color.FromRgb(236, 222, 196));

    /// <summary>Draws a placed opening with its selection-aware colour and a tag that makes it selectable.</summary>
    private void RenderOpeningSymbol(Wall wall, WallOpening opening)
    {
        var brush = opening.Id == _selectedOpeningId ? Brushes.DarkOrange
            : opening.Kind == OpeningKind.Door ? Brushes.SaddleBrown : Brushes.SteelBlue;
        DrawOpeningSymbol(wall, opening, brush, DoorLeafFill, opening.Id);
    }

    /// <summary>
    /// Draws one opening inside its host wall: the white cut through the wall, reveal lines at both
    /// jambs, then either glazing lines or a door leaf with its swing arc. A null tag makes the symbol a
    /// non-selectable preview, which is how an opening in motion is shown.
    /// </summary>
    private void DrawOpeningSymbol(Wall wall, WallOpening opening, Brush brush, Brush leafFill, Guid? tag)
    {
        var axisX = (wall.End.X - wall.Start.X) / wall.Length;
        var axisY = (wall.End.Y - wall.Start.Y) / wall.Length;
        // u runs along the wall from its start; v runs across it, toward the left-hand normal.
        /// <summary>Maps symbol coordinates onto the host axis.</summary>
        Point2 At(double u, double v) =>
            new(wall.Start.X + axisX * u - axisY * v, wall.Start.Y + axisY * u + axisX * v);
        var half = wall.Thickness / 2;

        PlanCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection(new[] { At(opening.Begin, -half), At(opening.End, -half),
                At(opening.End, half), At(opening.Begin, half) }.Select(WorldToCanvas)),
            Fill = Brushes.White, Tag = tag
        });
        if (opening.Kind == OpeningKind.Door)
        {
            DrawDoorSymbol(wall, opening, At, brush, leafFill, tag);
            return;
        }
        const double jamb = 0.06;
        foreach (var station in new[] { opening.Begin, opening.End - jamb })
        {
            PlanCanvas.Children.Add(new Polygon
            {
                Points = new PointCollection(new[] { At(station, -half), At(station + jamb, -half),
                    At(station + jamb, half), At(station, half) }.Select(WorldToCanvas)),
                Fill = leafFill, Stroke = brush, StrokeThickness = 1, Tag = tag
            });
            AddLine(At(station, -half - 0.025), At(station + jamb, -half - 0.025), 1, brush, tag);
            AddLine(At(station, half + 0.025), At(station + jamb, half + 0.025), 1, brush, tag);
        }

        // Two glazing lines sit a third of the way from the centreline to each face of the frame.
        var pane = half / 3;
        AddLine(At(opening.Begin, -pane), At(opening.End, -pane), 1.5, brush, tag);
        AddLine(At(opening.Begin, pane), At(opening.End, pane), 1.5, brush, tag);
    }

    /// <summary>
    /// Draws the shared door symbol: filled jamb linings, casings proud of both wall faces, threshold
    /// lines, each leaf open at 90° with its real thickness, and a fine continuous swing arc.
    /// </summary>
    private void DrawDoorSymbol(Wall wall, WallOpening opening, Func<double, double, Point2> at,
        Brush brush, Brush leafFill, Guid? tag)
    {
        var type = _session.Document.OpeningTypes.FirstOrDefault(candidate => candidate.Id == opening.OpeningTypeId);
        var symbol = DoorPlanSymbol.Build(opening.Begin, opening.End, wall.Thickness,
            type is null ? 0.04 : WallGeometry.LeafDepth(type, wall),
            type?.Operation ?? OpeningOperation.Standard, opening.FlipHand, opening.FlipFacing);
        Point ToCanvas((double U, double V) point) => WorldToCanvas(at(point.U, point.V));
        foreach (var line in symbol.Thresholds)
            PlanCanvas.Children.Add(new Line
            {
                X1 = ToCanvas(line.From).X, Y1 = ToCanvas(line.From).Y, X2 = ToCanvas(line.To).X, Y2 = ToCanvas(line.To).Y,
                Stroke = brush, StrokeThickness = 0.6, Tag = tag
            });
        foreach (var arc in symbol.Swings)
            PlanCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection(DoorPlanSymbol.Sample(arc).Select(ToCanvas)),
                Stroke = brush, StrokeThickness = 0.8, Tag = tag
            });
        foreach (var outline in symbol.Outlines)
            PlanCanvas.Children.Add(new Polygon
            {
                Points = new PointCollection(outline.Points.Select(ToCanvas)),
                Fill = outline.Part == DoorPlanPart.Casing ? Brushes.White : leafFill,
                Stroke = outline.Part == DoorPlanPart.Leaf ? brush : OpeningFrameBrush,
                StrokeThickness = outline.Part == DoorPlanPart.Leaf ? 1.4 : 1,
                StrokeLineJoin = PenLineJoin.Miter,
                SnapsToDevicePixels = true,
                Tag = tag
            });
    }

    /// <summary>Adds a model-space polyline, optionally dashed, to the plan canvas.</summary>
    private void AddPolyline(IEnumerable<Point2> points, double thickness, Brush color, Guid? tag, bool dashed)
    {
        PlanCanvas.Children.Add(new Polyline
        {
            Points = new PointCollection(points.Select(WorldToCanvas)),
            Stroke = color,
            StrokeThickness = thickness,
            StrokeDashArray = dashed ? new DoubleCollection { 4, 3 } : null,
            Tag = tag
        });
    }

    /// <summary>Selects the hosted instance instead of its wall when its plan symbol is clicked.</summary>
    private bool TrySelectOpeningSymbol(MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.Tag is not Guid id ||
            _session.Document.Openings.FirstOrDefault(opening => opening.Id == id) is not { } opening) return false;
        _selectedOpeningId = id;
        _selectedWallId = opening.WallId;
        _selectedWallOffset = opening.Offset;
        _selectedFloorId = _selectedRoomId = _selectedViewId = null;
        RefreshProperties();
        RenderPlan();
        return true;
    }

    /// <summary>Returns a frozen brush so static symbol colours can be shared across renders.</summary>
    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
