using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Site modeling: toposolids sketched like a floor, their elevation points, and how terrain reads in plan (contour
/// lines, spot elevations) and in elevation (the terrain profile).
/// </summary>
public partial class MainWindow
{
    /// <summary>Minor contour interval in meters; every fifth line is a thicker major contour.</summary>
    private const double ContourInterval = 0.5;

    /// <summary>True while clicks on a toposolid add elevation points.</summary>
    private bool _placingTopoPoints;

    /// <summary>Whether major contours carry their elevation, toggled by Label Contours.</summary>
    private bool _labelContours = true;

    /// <summary>Contours of each toposolid, kept until the toposolid record changes (records are replaced on edit).</summary>
    private readonly Dictionary<Guid, (FloorSlab Source, IReadOnlyList<ContourSegment> Lines)> _contourCache = [];

    /// <summary>Asks for the elevation of a new point; replaced by the interaction checks.</summary>
    internal Func<double, double?> AskTopoElevation { get; set; } = null!;

    /// <summary>Starts sketching a toposolid boundary, closed with a double click or a right click, as in Revit.</summary>
    private void BeginToposolidSketch(bool rectangle)
    {
        var preset = ArchitecturalLibrary.SlabPresets.First(slab => slab.Kind == FloorKind.Toposolid);
        BeginSlabSketch(preset with { Name = GetText("Entity.Toposolid") }, rectangle);
        StatusText.Text = GetText(rectangle ? "Status.ToposolidRectangle" : "Status.ToposolidSketch");
    }

    /// <summary>Arms the elevation point tool: each click on a toposolid asks for the elevation there.</summary>
    private void BeginTopoPointTool()
    {
        CancelStructuralTool();
        SetWallTool(false);
        ViewTabs.SelectedIndex = 0;
        if (!_session.Document.Floors.Any(floor => floor.IsToposolid && floor.LevelId == _activeLevelId))
        {
            StatusText.Text = GetText("Status.TopoPointNoToposolid");
            return;
        }
        _placingTopoPoints = true;
        StatusText.Text = GetText("Status.TopoPointPick");
        RenderPlan();
    }

    /// <summary>Places an elevation point where the plan was clicked, asking for its elevation; false when the tool is off.</summary>
    private bool TryHandleTopoPointClick(MouseButtonEventArgs e)
    {
        if (!_placingTopoPoints) return false;
        var point = CanvasToWorld(PlanPosition(e));
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Ctrl+click removes the nearest point instead, like deleting a survey point.
            var removed = _session.Document.Floors.Where(floor => floor.IsToposolid && floor.LevelId == _activeLevelId)
                .Any(floor => _session.RemoveTopoPoint(floor.Id, point, 12 / _scale));
            StatusText.Text = GetText(removed ? "Status.TopoPointRemoved" : "Status.TopoPointPick");
            RefreshAll();
            return true;
        }
        var toposolid = ToposolidAt(point);
        if (toposolid is null)
        {
            StatusText.Text = GetText("Status.TopoPointOutside");
            return true;
        }
        var current = new TopoSurface(toposolid).ElevationAt(point.X, point.Y);
        if ((AskTopoElevation ?? AskTopoElevationWithPrompt)(current) is { } elevation) PlaceTopoPoint(toposolid.Id, point, elevation);
        return true;
    }

    /// <summary>Adds the point in one undoable step and redraws every view.</summary>
    private void PlaceTopoPoint(Guid toposolidId, Point2 point, double elevation)
    {
        try
        {
            _session.AddTopoPoint(toposolidId, new TopoPoint(point.X, point.Y, elevation));
            StatusText.Text = FormatText("Status.TopoPointAdded", elevation);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
    }

    private double? AskTopoElevationWithPrompt(double current)
    {
        var answer = PromptWindow.Ask(this, GetText("Dialog.TopoPointElevation"),
            current.ToString("0.00", CultureInfo.CurrentCulture));
        if (answer is null) return null;
        if (double.TryParse(answer.Replace(" m", string.Empty), NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            double.IsFinite(value) && value is > -1000 and < 5000)
            return value;
        StatusText.Text = GetText("Status.WallInvalidOptions");
        return null;
    }

    /// <summary>Leaves the elevation point tool; true when it was armed.</summary>
    private bool CancelTopoPointTool()
    {
        if (!_placingTopoPoints) return false;
        _placingTopoPoints = false;
        StatusText.Text = GetText("Status.SelectionMode");
        RenderPlan();
        return true;
    }

    private FloorSlab? ToposolidAt(Point2 point) => _session.Document.Floors.FirstOrDefault(floor =>
        floor.IsToposolid && floor.LevelId == _activeLevelId && TopographyGeometry.Contains(floor.Boundary, point));

    /// <summary>
    /// Draws a toposolid in plan: a pale site fill, contour lines (thin every 0.5 m, thicker every 2.5 m with their
    /// elevation), and its elevation points as crosses with their spot elevation.
    /// </summary>
    private void AddToposolidPlan(FloorSlab toposolid)
    {
        var selected = toposolid.Id == _selectedFloorId;
        PlanCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection(toposolid.Boundary.Select(WorldToCanvas)),
            Fill = new SolidColorBrush(Color.FromArgb(55, 130, 170, 96)),
            Stroke = selected ? Brushes.DarkOrange : new SolidColorBrush(Color.FromRgb(70, 110, 60)),
            StrokeThickness = selected ? 2.5 : 1.2,
            Tag = toposolid.Id
        });
        if (!_contourCache.TryGetValue(toposolid.Id, out var cached) || !ReferenceEquals(cached.Source, toposolid))
        {
            cached = (toposolid, TopographyGeometry.Contours(toposolid, ContourInterval));
            _contourCache[toposolid.Id] = cached;
        }
        var minor = new SolidColorBrush(Color.FromArgb(150, 120, 104, 82));
        var major = new SolidColorBrush(Color.FromRgb(110, 86, 58));
        var labelled = new HashSet<double>();
        foreach (var segment in cached.Lines)
        {
            var isMajor = Math.Abs(segment.Elevation / (ContourInterval * 5) - Math.Round(segment.Elevation / (ContourInterval * 5))) < 1e-6;
            var start = WorldToCanvas(segment.Start);
            var end = WorldToCanvas(segment.End);
            PlanCanvas.Children.Add(new Line
            {
                X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
                Stroke = isMajor ? major : minor, StrokeThickness = isMajor ? 1.3 : 0.6, IsHitTestVisible = false
            });
            // One label per major contour, on its first piece long enough to read.
            if (!_labelContours || !isMajor || (end - start).Length < 6 || !labelled.Add(segment.Elevation)) continue;
            var label = new TextBlock
            {
                Text = segment.Elevation.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture),
                FontSize = 9, Foreground = major, Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, (start.X + end.X) / 2 + 2);
            Canvas.SetTop(label, (start.Y + end.Y) / 2 - 7);
            PlanCanvas.Children.Add(label);
        }
        foreach (var point in toposolid.TopoPoints ?? [])
        {
            var center = WorldToCanvas(point.Plan);
            foreach (var (dx, dy) in new[] { (-4.0, -4.0), (-4.0, 4.0) })
                PlanCanvas.Children.Add(new Line
                {
                    X1 = center.X + dx, Y1 = center.Y + dy, X2 = center.X - dx, Y2 = center.Y - dy,
                    Stroke = Brushes.SaddleBrown, StrokeThickness = 1.2, IsHitTestVisible = false
                });
            var spot = new TextBlock
            {
                Text = point.Elevation.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture),
                FontSize = 9.5, Foreground = Brushes.SaddleBrown, IsHitTestVisible = false
            };
            Canvas.SetLeft(spot, center.X + 5);
            Canvas.SetTop(spot, center.Y - 14);
            PlanCanvas.Children.Add(spot);
        }
    }

    /// <summary>
    /// Draws a pitched roof in an elevation or section as its silhouette from that side: from the eaves, with the
    /// overhang, up to the ridge, filled with the roof's thickness below the surface.
    /// </summary>
    private void DrawRoofProfile(Canvas canvas, FloorSlab roof, double levelElevation, ViewOrientation orientation,
        double minimumCoordinate, double minimumElevation, double horizontalOffset, double scale)
    {
        var built = RoofGeometry.WithOverhang(roof);
        var eave = levelElevation + built.ElevationOffset;
        var (minX, maxX, minY, maxY) = RoofGeometry.Extent(built.Boundary);
        var spacing = Math.Max(0.05, Math.Max(maxX - minX, maxY - minY) / 120);
        var top = new SortedDictionary<int, double>();
        var bottom = new SortedDictionary<int, double>();
        for (var x = minX; x <= maxX + 1e-9; x += spacing)
            for (var y = minY; y <= maxY + 1e-9; y += spacing)
            {
                var point = new Point2(x, y);
                if (!TopographyGeometry.Contains(built.Boundary, point)) continue;
                var surface = eave + RoofGeometry.RiseAt(built, point);
                var bin = (int)Math.Round(ProjectCoordinate(point, orientation) / spacing);
                top[bin] = top.TryGetValue(bin, out var high) ? Math.Max(high, surface) : surface;
                bottom[bin] = bottom.TryGetValue(bin, out var low) ? Math.Min(low, surface - roof.Thickness) : surface - roof.Thickness;
            }
        if (top.Count < 2) return;
        var outline = new PointCollection();
        foreach (var (bin, elevation) in top)
            outline.Add(new Point(horizontalOffset + (bin * spacing - minimumCoordinate) * scale,
                ProjectElevation(canvas, elevation, minimumElevation, scale)));
        foreach (var (bin, elevation) in bottom.Reverse())
            outline.Add(new Point(horizontalOffset + (bin * spacing - minimumCoordinate) * scale,
                ProjectElevation(canvas, Math.Min(elevation, top[bin] - roof.Thickness), minimumElevation, scale)));
        canvas.Children.Add(new Polygon
        {
            Points = outline,
            Fill = new SolidColorBrush(Color.FromRgb(176, 98, 70)),
            Stroke = Brushes.DarkSlateGray,
            StrokeThickness = 1
        });
    }

    /// <summary>
    /// Draws a toposolid in an elevation or section: the skyline of its surface seen from that side, filled down to its
    /// flat bottom, so hills and slopes read as they do in Revit's elevations.
    /// </summary>
    private void DrawToposolidProfile(Canvas canvas, FloorSlab toposolid, double levelElevation, ViewOrientation orientation,
        double minimumCoordinate, double minimumElevation, double horizontalOffset, double scale)
    {
        var surface = new TopoSurface(toposolid);
        var spacing = TopographyGeometry.GridSpacing(toposolid.Boundary);
        var minX = toposolid.Boundary.Min(point => point.X);
        var maxX = toposolid.Boundary.Max(point => point.X);
        var minY = toposolid.Boundary.Min(point => point.Y);
        var maxY = toposolid.Boundary.Max(point => point.Y);
        var bins = new SortedDictionary<int, double>();
        var low = double.MaxValue;
        for (var x = minX; x <= maxX + 1e-9; x += spacing)
            for (var y = minY; y <= maxY + 1e-9; y += spacing)
            {
                var point = new Point2(x, y);
                if (!TopographyGeometry.Contains(toposolid.Boundary, point)) continue;
                var elevation = surface.ElevationAt(x, y);
                var bin = (int)Math.Round(ProjectCoordinate(point, orientation) / spacing);
                bins[bin] = bins.TryGetValue(bin, out var existing) ? Math.Max(existing, elevation) : elevation;
                low = Math.Min(low, elevation);
            }
        if (bins.Count < 2) return;
        var bottom = levelElevation + low - toposolid.Thickness;
        var outline = new PointCollection();
        foreach (var (bin, elevation) in bins)
            outline.Add(new Point(horizontalOffset + (bin * spacing - minimumCoordinate) * scale,
                ProjectElevation(canvas, levelElevation + elevation, minimumElevation, scale)));
        var lastX = outline[^1].X;
        var firstX = outline[0].X;
        outline.Add(new Point(lastX, ProjectElevation(canvas, bottom, minimumElevation, scale)));
        outline.Add(new Point(firstX, ProjectElevation(canvas, bottom, minimumElevation, scale)));
        canvas.Children.Add(new Polygon
        {
            Points = outline,
            Fill = new SolidColorBrush(Color.FromRgb(196, 178, 146)),
            Stroke = new SolidColorBrush(Color.FromRgb(96, 120, 70)),
            StrokeThickness = 1.5
        });
    }
}
