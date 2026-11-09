using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Draws every structural element of the active level, selected ones highlighted.</summary>
    private void RenderStructuralPlanElements()
    {
        foreach (var element in _session.Document.StructuralElements.Where(candidate => candidate.LevelId == _activeLevelId))
        {
            var selected = element.Id == _selectedStructuralId;
            if (element.IsDatumLine)
            {
                AddDatumLine(element, selected);
                continue;
            }
            if (element.Kind == StructuralElementKind.Area)
            {
                AddAreaOutline(element, selected);
                continue;
            }
            if (element.Kind is StructuralElementKind.SpotElevation or StructuralElementKind.SpotCoordinate or StructuralElementKind.SpotSlope
                or StructuralElementKind.Keynote or StructuralElementKind.Symbol or StructuralElementKind.ColorLegend)
            {
                AddAnnotationMark(element, selected);
                continue;
            }
            if (element.Kind == StructuralElementKind.ViewReference)
            {
                AddViewReferenceSymbol(element, selected);
                continue;
            }
            if (element.Kind is StructuralElementKind.FilledRegion or StructuralElementKind.RevisionCloud or StructuralElementKind.Insulation or StructuralElementKind.ParkingSpace)
            {
                AddDetailSymbol(element, selected);
                continue;
            }
            if (element.Kind == StructuralElementKind.Planting)
            {
                AddPlantingSymbol(element, selected, preview: false);
                continue;
            }
            if (element.Kind == StructuralElementKind.ModelText)
            {
                var origin = WorldToCanvas(element.Start);
                var label = new TextBlock
                {
                    Text = element.Name,
                    FontSize = 12,
                    Foreground = selected ? Brushes.DarkOrange : Brushes.Black,
                    Tag = element.Id
                };
                Canvas.SetLeft(label, origin.X);
                Canvas.SetTop(label, origin.Y);
                PlanCanvas.Children.Add(label);
                continue;
            }
            if (HasDetailedPlanParts(element))
            {
                foreach (var part in ElementGeometry.Parts(element))
                    PlanCanvas.Children.Add(new Polygon { Points = new PointCollection(part.Footprint.Select(WorldToCanvas)),
                        Fill = PartPlanFill(element, part), Stroke = selected ? Brushes.DarkOrange : Brushes.DimGray,
                        StrokeThickness = selected ? 1.5 : 1, Tag = element.Id });
                if (element.Kind == StructuralElementKind.Ramp) AddRampArrow(element, selected);
                if (element.Kind == StructuralElementKind.Stair) AddStairWalkLine(element, selected);
                continue;
            }
            var isHole = element.Kind is StructuralElementKind.Opening or StructuralElementKind.Shaft;
            PlanCanvas.Children.Add(new Polygon
            {
                Points = new PointCollection(element.Footprint.Select(WorldToCanvas)),
                Fill = new SolidColorBrush(StructuralPlanFill(element.Kind)),
                Stroke = selected ? Brushes.DarkOrange : Brushes.DimGray,
                StrokeThickness = selected ? 2.5 : 1,
                StrokeDashArray = isHole ? new DoubleCollection { 4, 3 } : null,
                Tag = element.Id
            });
        }
    }

    /// <summary>Kinds and styles whose plan symbol is more than one footprint.</summary>
    private static bool HasDetailedPlanParts(StructuralElement element) =>
        element.Kind is StructuralElementKind.Stair or StructuralElementKind.Railing or StructuralElementKind.Ramp
            or StructuralElementKind.Gutter or StructuralElementKind.CurtainWall or StructuralElementKind.Skylight ||
        (element.HasSolid && element.Style != ElementStyle.Default);

    /// <summary>Glass parts read as pale blue; metal drainage as grey; everything else keeps the neutral part fill.</summary>
    private static Brush PartPlanFill(StructuralElement element, ElementPart part) =>
        part.Kind == Core.Geometry.SurfaceKind.Glass ? new SolidColorBrush(Color.FromArgb(120, 150, 205, 230))
        : element.Kind is StructuralElementKind.Gutter or StructuralElementKind.Downspout ? Brushes.LightSlateGray
        : element.Kind == StructuralElementKind.Ramp ? new SolidColorBrush(Color.FromArgb(170, 205, 190, 150))
        : Brushes.WhiteSmoke;

    /// <summary>Draws the slope direction of a ramp: an arrow from its foot to its head.</summary>
    private void AddRampArrow(StructuralElement ramp, bool selected)
    {
        var brush = selected ? Brushes.DarkOrange : Brushes.DimGray;
        AddLine(ramp.Start, ramp.End, 1, brush, ramp.Id);
        var length = ramp.Length;
        if (length < 0.2) return;
        var direction = new Point2((ramp.End.X - ramp.Start.X) / length, (ramp.End.Y - ramp.Start.Y) / length);
        var head = Math.Min(0.3, length / 4);
        foreach (var side in new[] { -1, 1 })
            AddLine(ramp.End, new Point2(ramp.End.X - direction.X * head - side * direction.Y * head * 0.5,
                ramp.End.Y - direction.Y * head + side * direction.X * head * 0.5), 1, brush, ramp.Id);
    }

    /// <summary>
    /// Draws a stair's walking line as Revit does: a line through the middle of every flight and landing from the first
    /// riser, ending in an arrow at the top, with "SUBE" at the foot.
    /// </summary>
    private void AddStairWalkLine(StructuralElement stair, bool selected)
    {
        var brush = selected ? Brushes.DarkOrange : Brushes.Black;
        var path = StairGeometry.WalkLine(stair);
        if (path.Count < 2) return;
        for (var index = 0; index + 1 < path.Count; index++)
            if (path[index].DistanceTo(path[index + 1]) > 1e-6) AddLine(path[index], path[index + 1], 1, brush, stair.Id);
        var tip = path[^1];
        var before = path.Take(path.Count - 1).Last(point => point.DistanceTo(tip) > 1e-6);
        var length = before.DistanceTo(tip);
        var direction = new Point2((tip.X - before.X) / length, (tip.Y - before.Y) / length);
        var head = Math.Min(0.3, stair.Width / 3);
        foreach (var side in new[] { -1, 1 })
            AddLine(tip, new Point2(tip.X - direction.X * head - side * direction.Y * head * 0.5,
                tip.Y - direction.Y * head + side * direction.X * head * 0.5), 1, brush, stair.Id);
        var foot = WorldToCanvas(path[0]);
        var label = new TextBlock { Text = GetText("Plan.StairUp"), FontSize = 9, Foreground = brush, Tag = stair.Id };
        Canvas.SetLeft(label, foot.X + 3);
        Canvas.SetTop(label, foot.Y + 2);
        PlanCanvas.Children.Add(label);
        var dot = new Ellipse { Width = 5, Height = 5, Fill = brush, Tag = stair.Id };
        Canvas.SetLeft(dot, foot.X - 2.5);
        Canvas.SetTop(dot, foot.Y - 2.5);
        PlanCanvas.Children.Add(dot);
    }

    /// <summary>Draws a reference plane, room separator, area boundary or model line in its own drafting style.</summary>
    private void AddDatumLine(StructuralElement element, bool selected)
    {
        var (brush, thickness, dashes) = element.Kind switch
        {
            StructuralElementKind.ReferencePlane => (Brushes.ForestGreen, 1.0, new DoubleCollection { 10, 3, 2, 3 }),
            StructuralElementKind.RoomSeparator => (Brushes.MediumVioletRed, 1.5, new DoubleCollection { 6, 2 }),
            StructuralElementKind.AreaBoundary => (Brushes.Firebrick, 1.5, new DoubleCollection { 2, 2 }),
            StructuralElementKind.DetailLine => (Brushes.Black, 1.0, (DoubleCollection?)null),
            StructuralElementKind.Matchline => (Brushes.Purple, 3.0, new DoubleCollection { 10, 3, 2, 3 }),
            _ => (Brushes.Black, 1.5, (DoubleCollection?)null)
        };
        var start = WorldToCanvas(element.Start);
        var end = WorldToCanvas(element.End);
        PlanCanvas.Children.Add(new Line
        {
            X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
            Stroke = selected ? Brushes.DarkOrange : brush,
            StrokeThickness = selected || element.Id == _activeWorkPlaneId ? thickness + 1.5 : thickness,
            StrokeDashArray = dashes,
            Tag = element.Id
        });
        if (element.Kind is not (StructuralElementKind.ReferencePlane or StructuralElementKind.Matchline) || string.IsNullOrWhiteSpace(element.Name)) return;
        var label = new TextBlock { Text = element.Name, FontSize = 10, Foreground = element.Kind == StructuralElementKind.Matchline ? Brushes.Purple : Brushes.ForestGreen, Tag = element.Id };
        Canvas.SetLeft(label, start.X + 3);
        Canvas.SetTop(label, start.Y - 14);
        PlanCanvas.Children.Add(label);
    }

    /// <summary>The hatching of a filled region: thin diagonals repeating every 8 pixels.</summary>
    private static readonly Brush DetailHatch = CreateDetailHatch();

    private static Brush CreateDetailHatch()
    {
        var lines = new GeometryDrawing(null, new Pen(Brushes.DimGray, 0.7), Geometry.Parse("M0,8 L8,0 M-1,1 L1,-1 M7,9 L9,7"));
        var brush = new DrawingBrush(lines)
        {
            TileMode = TileMode.Tile, Viewbox = new Rect(0, 0, 8, 8), ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Draws the plan-only drafting symbols: a region hatched with diagonals, a revision cloud with scallops on its
    /// sides, and insulation as batting waves along its line.
    /// </summary>
    private void AddDetailSymbol(StructuralElement element, bool selected)
    {
        var stroke = selected ? Brushes.DarkOrange : element.Kind == StructuralElementKind.RevisionCloud ? Brushes.Firebrick : Brushes.Black;
        var thickness = selected ? 2.5 : 1.2;
        if (element.Kind == StructuralElementKind.ParkingSpace)
        {
            var corners = element.Footprint.Select(WorldToCanvas).ToArray();
            PlanCanvas.Children.Add(new Polygon
            {
                Points = new PointCollection(corners), Fill = new SolidColorBrush(Color.FromArgb(40, 70, 110, 190)), Stroke = selected ? Brushes.DarkOrange : Brushes.SteelBlue,
                StrokeThickness = selected ? 2.5 : 1.4, Tag = element.Id
            });
            var middle = new Point(corners.Average(point => point.X), corners.Average(point => point.Y));
            var mark = new TextBlock { Text = "P", FontWeight = FontWeights.Bold, FontSize = 16, Foreground = Brushes.SteelBlue, IsHitTestVisible = false };
            Canvas.SetLeft(mark, middle.X - 6);
            Canvas.SetTop(mark, middle.Y - 11);
            PlanCanvas.Children.Add(mark);
            return;
        }
        if (element.Kind == StructuralElementKind.FilledRegion)
        {
            PlanCanvas.Children.Add(new Polygon
            {
                Points = new PointCollection(element.Footprint.Select(WorldToCanvas)), Fill = DetailHatch,
                Stroke = stroke, StrokeThickness = thickness, Tag = element.Id
            });
            return;
        }
        if (element.Kind == StructuralElementKind.RevisionCloud)
        {
            var corners = element.Footprint.Select(WorldToCanvas).ToArray();
            var area = 0.0;
            for (var index = 0; index < corners.Length; index++)
            {
                var next = corners[(index + 1) % corners.Length];
                area += corners[index].X * next.Y - next.X * corners[index].Y;
            }
            // Shoelace area in a y-down canvas is positive for a clockwise outline, where a clockwise arc bulges outward.
            var sweep = area > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
            var figure = new PathFigure { StartPoint = corners[0], IsClosed = true, IsFilled = false };
            for (var index = 0; index < corners.Length; index++)
            {
                var from = corners[index];
                var to = corners[(index + 1) % corners.Length];
                var scallops = Math.Max(1, (int)Math.Round((to - from).Length / 16));
                var radius = (to - from).Length / scallops * 0.55;
                for (var step = 1; step <= scallops; step++)
                    figure.Segments.Add(new ArcSegment(from + (to - from) * step / scallops, new Size(radius, radius), 0, false, sweep, true));
            }
            PlanCanvas.Children.Add(new Path { Data = new PathGeometry([figure]), Stroke = stroke, StrokeThickness = thickness, Tag = element.Id });
            return;
        }
        var start = WorldToCanvas(element.Start);
        var end = WorldToCanvas(element.End);
        var span = end - start;
        if (span.Length < 1) return;
        var along = span / span.Length;
        var across = new Vector(-along.Y, along.X);
        // The waves are as tall as the insulation is thick, and never so small that they vanish when zoomed out.
        var amplitude = Math.Max(3, element.Width * (WorldToCanvas(new Point2(1, 0)) - WorldToCanvas(new Point2(0, 0))).Length / 2);
        var wave = new PointCollection();
        var samples = Math.Max(2, (int)(span.Length / (amplitude * 0.5)));
        for (var step = 0; step <= samples; step++)
        {
            var distance = span.Length * step / samples;
            wave.Add(start + along * distance + across * (amplitude * Math.Sin(distance / amplitude * Math.PI)));
        }
        PlanCanvas.Children.Add(new Polyline { Points = wave, Stroke = stroke, StrokeThickness = selected ? 2 : 1, Tag = element.Id });
        PlanCanvas.Children.Add(new Line
        {
            X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, Stroke = Brushes.Transparent,
            StrokeThickness = Math.Max(8, amplitude * 2), Tag = element.Id
        });
    }

    /// <summary>Draws an area of the area scheme: a dashed rectangle labelled with its name and measured area.</summary>
    private void AddAreaOutline(StructuralElement area, bool selected)
    {
        PlanCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection(area.Footprint.Select(WorldToCanvas)),
            Fill = new SolidColorBrush(Color.FromArgb(40, 205, 120, 60)),
            Stroke = selected ? Brushes.DarkOrange : Brushes.Chocolate,
            StrokeThickness = selected ? 2.5 : 1.2,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            Tag = area.Id
        });
        var center = WorldToCanvas(new Point2((area.Start.X + area.End.X) / 2, (area.Start.Y + area.End.Y) / 2));
        var label = new TextBlock
        {
            Text = $"{area.Name}\n{Units.FormatArea(area.PlanArea)}",
            TextAlignment = TextAlignment.Center,
            FontSize = 11,
            Foreground = Brushes.Chocolate,
            Tag = area.Id
        };
        Canvas.SetLeft(label, center.X - 40);
        Canvas.SetTop(label, center.Y - 14);
        PlanCanvas.Children.Add(label);
    }

    /// <summary>Shows the armed element as a dashed outline at the pointer, or between the first and second click.</summary>
    private void RenderStructuralPreview()
    {
        if (_structuralTool is not { } tool || _structuralPreviewPoint is not Point2 pointer) return;
        if (tool.Mode == StructuralPlacementMode.Rectangle)
        {
            var corner = _pendingStructuralStart ?? new Point2(pointer.X - 1, pointer.Y - 1);
            var corners = new[] { corner, new Point2(pointer.X, corner.Y), pointer, new Point2(corner.X, pointer.Y) };
            AddStructuralPreviewOutline(corners);
            return;
        }

        var start = tool.Mode == StructuralPlacementMode.Point ? pointer :
            _pendingStructuralStart ?? new Point2(pointer.X - 1, pointer.Y);
        var origin = start;
        var (width, height, offset) = StructuralElementStandards.For(tool.Kind);
        var style = ElementStyle.Default;
        var (depth, shape) = (0.0, ResidentialComponentShape.Box);
        if (_structuralPreset is { } preset && preset.Kind == tool.Kind)
            (width, height, offset, style, depth, shape) =
                (preset.Width, preset.Height, preset.Offset, preset.Style, preset.Depth, preset.ComponentShape);
        if (tool.Kind == StructuralElementKind.Stair) height = StairPlacementHeight(height, offset);
        var preview = new StructuralElement(Guid.Empty, _activeLevelId, tool.Kind, string.Empty,
            origin, pointer, width, height, offset)
        {
            Style = style, Depth = depth, ComponentShape = shape,
            Rotation = tool.Kind == StructuralElementKind.Component ? _componentRotation : 0
        };
        if (tool.Kind == StructuralElementKind.Planting)
            AddPlantingSymbol(preview, selected: false, preview: true);
        else if (tool.Kind is StructuralElementKind.ReferencePlane or StructuralElementKind.RoomSeparator
            or StructuralElementKind.AreaBoundary or StructuralElementKind.ModelLine or StructuralElementKind.DetailLine or StructuralElementKind.Matchline
            or StructuralElementKind.SpotSlope)
            AddStructuralPreviewOutline([origin, pointer]);
        else if (HasDetailedPlanParts(preview))
            foreach (var part in ElementGeometry.Parts(preview).Take(400)) AddStructuralPreviewOutline(part.Footprint);
        else AddStructuralPreviewOutline(preview.Footprint);
    }

    /// <summary>Adds a dashed, non-interactive outline for the provisional structural geometry.</summary>
    private void AddStructuralPreviewOutline(IEnumerable<Point2> outline) =>
        PlanCanvas.Children.Add(new Polygon
        {
            Points = new PointCollection(outline.Select(WorldToCanvas)),
            Stroke = Brushes.DarkOrange,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false
        });

    /// <summary>Shows the element's name, section, offset, and length in the properties panel.</summary>
    private bool TryRefreshStructuralProperties()
    {
        var element = _session.Document.StructuralElements.FirstOrDefault(candidate => candidate.Id == _selectedStructuralId);
        if (element is null) return false;
        PropertiesText.Text = element.Kind == StructuralElementKind.Stair ? StairProperties(element) :
            FormatText("Properties.StructuralSummary", element.Name, element.Width, element.Height, element.Offset, element.Length);
        return true;
    }

    /// <summary>
    /// Draws a planting the way a Revit site plan does: a translucent canopy with a scalloped edge at its full spread, a
    /// small trunk circle, and the mark of its growth (palm fronds, conifer spikes). The preview is the same symbol,
    /// dashed and without hit testing.
    /// </summary>
    private void AddPlantingSymbol(StructuralElement plant, bool selected, bool preview)
    {
        var center = plant.Start;
        var radius = plant.Width / 2;
        var stroke = selected ? Brushes.DarkOrange : preview ? Brushes.DarkOrange
            : new SolidColorBrush(Color.FromRgb(46, 98, 44));
        var (lobes, depth) = plant.Style switch
        {
            ElementStyle.Palm => (13, 0.22),
            ElementStyle.Conifer => (16, 0.18),
            ElementStyle.Shrub => (7, 0.10),
            ElementStyle.Guanacaste => (18, 0.06),
            _ => (11, 0.08)
        };
        var outline = new PointCollection();
        const int samples = 144;
        for (var index = 0; index < samples; index++)
        {
            var angle = index * Math.PI * 2 / samples;
            // Rounded scallops for broadleaf canopies; sharp points for palm fronds and conifer branches.
            var wave = Math.Abs(Math.Sin(angle * lobes / 2));
            var shape = plant.Style is ElementStyle.Palm or ElementStyle.Conifer ? Math.Pow(wave, 6) : Math.Sqrt(wave);
            var r = radius * (1 - depth + depth * shape);
            outline.Add(WorldToCanvas(new Point2(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle))));
        }
        PlanCanvas.Children.Add(new Polygon
        {
            Points = outline,
            Fill = preview ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(70, 96, 156, 72)),
            Stroke = stroke,
            StrokeThickness = selected ? 2 : 1,
            StrokeDashArray = preview ? new DoubleCollection { 4, 3 } : null,
            IsHitTestVisible = !preview,
            Tag = preview ? null : plant.Id
        });
        if (plant.Style is ElementStyle.Palm or ElementStyle.Conifer)
            for (var index = 0; index < lobes; index++)
            {
                var angle = (index + 0.5) * Math.PI * 2 / lobes;
                var tip = new Point2(center.X + radius * 0.9 * Math.Cos(angle), center.Y + radius * 0.9 * Math.Sin(angle));
                var from = WorldToCanvas(center);
                var to = WorldToCanvas(tip);
                PlanCanvas.Children.Add(new Line
                {
                    X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = stroke, StrokeThickness = 0.8,
                    IsHitTestVisible = false
                });
            }
        var trunk = Math.Max(0.06, Core.Geometry.PlantGeometry.Proportions(plant.Style, plant.Height, plant.Width).TrunkRadius)
                    * _scale;
        if (plant.Style == ElementStyle.Shrub) return;
        var centerPoint = WorldToCanvas(center);
        var trunkMark = new Ellipse
        {
            Width = trunk * 2, Height = trunk * 2, Stroke = stroke, StrokeThickness = 1,
            Fill = preview ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(120, 94, 66)),
            IsHitTestVisible = !preview, Tag = preview ? null : plant.Id
        };
        Canvas.SetLeft(trunkMark, centerPoint.X - trunk);
        Canvas.SetTop(trunkMark, centerPoint.Y - trunk);
        PlanCanvas.Children.Add(trunkMark);
    }

    /// <summary>Gives each structural kind a distinct, semi-transparent plan fill.</summary>
    private static Color StructuralPlanFill(StructuralElementKind kind) => kind switch
    {
        StructuralElementKind.Beam => Color.FromArgb(150, 139, 90, 43),
        StructuralElementKind.Truss => Color.FromArgb(150, 160, 120, 40),
        StructuralElementKind.Brace => Color.FromArgb(150, 150, 60, 50),
        StructuralElementKind.WallFoundation => Color.FromArgb(120, 120, 120, 120),
        StructuralElementKind.IsolatedFoundation => Color.FromArgb(120, 150, 150, 150),
        StructuralElementKind.Component => Color.FromArgb(150, 90, 100, 110),
        // Openings and shafts show as holes: the slab underneath is still drawn, so the fill is nearly white.
        StructuralElementKind.Opening or StructuralElementKind.Shaft => Color.FromArgb(235, 255, 255, 255),
        StructuralElementKind.Dormer => Color.FromArgb(160, 150, 96, 70),
        StructuralElementKind.Skylight => Color.FromArgb(150, 150, 200, 225),
        _ => Color.FromArgb(200, 200, 40, 40)
    };
}
