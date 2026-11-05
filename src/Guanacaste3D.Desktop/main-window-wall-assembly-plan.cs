using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// High-zoom construction drafting over a joined wall footprint. This overlay never owns mouse hits;
    /// the underlying wall remains selectable. The true footprint clips course markers at mitered joints.
    /// </summary>
    private void RenderWallAssemblyPlan(Wall wall, WallType type, IReadOnlyList<Point2> footprint)
    {
        if (wall.Length <= 0.001 || _scale <= 0) return;
        // An instance can be thinner than its type: never crash the plan viewport for an invalid overlay.
        var finishTotal = type.LeftRepelloThickness + type.LeftFineThickness +
                          type.RightRepelloThickness + type.RightFineThickness;
        if (finishTotal >= wall.Thickness) return;
        var assembly = WallAssemblyPlanRules.For(type, wall.Thickness);
        var dx = (wall.End.X - wall.Start.X) / wall.Length;
        var dy = (wall.End.Y - wall.Start.Y) / wall.Length;
        var nx = -dy;
        var ny = dx;
        Point2 At(double along, double across) =>
            new(wall.Start.X + along * dx + across * nx,
                wall.Start.Y + along * dy + across * ny);

        var polygon = new PathFigure
        {
            StartPoint = WorldToCanvas(footprint[0]), IsClosed = true, IsFilled = true
        };
        polygon.Segments.Add(new PolyLineSegment(footprint.Skip(1).Select(WorldToCanvas), true));
        var overlay = new Canvas
        {
            Width = PlanCanvas.ActualWidth,
            Height = PlanCanvas.ActualHeight,
            Clip = new PathGeometry(new[] { polygon }),
            IsHitTestVisible = false
        };
        void Stroke(double a1, double offset1, double a2, double offset2, Brush color, double px)
        {
            var first = WorldToCanvas(At(a1, offset1));
            var second = WorldToCanvas(At(a2, offset2));
            overlay.Children.Add(new Line
            {
                X1 = first.X, Y1 = first.Y, X2 = second.X, Y2 = second.Y,
                Stroke = color, StrokeThickness = px, SnapsToDevicePixels = true
            });
        }

        // Fine is the outermost thin skin; the repello boundary is thicker inside it.
        foreach (var offset in WallAssemblyPlanRules.LayerOffsets(assembly))
            Stroke(0, offset, wall.Length, offset, Brushes.DimGray, 0.85);

        // Microscopic hatching becomes noise at overview scales.
        if (_scale < 100) { PlanCanvas.Children.Add(overlay); return; }
        var left = -wall.Thickness / 2 + assembly.LeftFinish;
        var right = wall.Thickness / 2 - assembly.RightFinish;
        if (right <= left) { PlanCanvas.Children.Add(overlay); return; }

        var openings = _session.Document.Openings.Where(o => o.WallId == wall.Id &&
            o.Sill <= 1.2 && o.Sill + o.Height >= 1.2).ToArray();
        bool InOpening(double along) => openings.Any(o => along >= o.Begin && along <= o.End);

        if (assembly.IsMasonry)
        {
            var pitch = assembly.Construction == WallConstruction.Brick ? 0.24 : 0.40;
            var ink = assembly.Construction == WallConstruction.Brick
                ? new SolidColorBrush(Color.FromRgb(130, 65, 43))
                : Brushes.DimGray;
            var lines = Math.Min(3000, (int)Math.Floor(wall.Length / pitch));
            for (var i = 1; i <= lines; i++)
            {
                var position = i * pitch;
                if (position >= wall.Length || InOpening(position)) continue;
                Stroke(position, left, position, right, ink, 0.8);
            }
            // An exposed concrete block shows its pair of void cells at close-up,
            // while brick stays solid with joints.
            if (assembly.Construction == WallConstruction.Block && _scale >= 210 &&
                right - left >= 0.10)
            {
                for (var index = 0; index < Math.Min(lines + 1, 3000); index++)
                {
                    var baseAlong = index * pitch;
                    for (var cell = 0; cell < 2; cell++)
                    {
                        var cellCentre = baseAlong + (cell + 0.5) * pitch / 2;
                        if (cellCentre >= wall.Length - 0.025 || InOpening(cellCentre)) continue;
                        var halfLength = Math.Min(0.065, pitch * 0.19);
                        var halfWidth = Math.Min(0.048, (right - left) * 0.32);
                        var corners = new[]
                        {
                            At(cellCentre - halfLength, -halfWidth),
                            At(cellCentre + halfLength, -halfWidth),
                            At(cellCentre + halfLength, halfWidth),
                            At(cellCentre - halfLength, halfWidth)
                        };
                        overlay.Children.Add(new Polygon
                        {
                            Points = new PointCollection(corners.Select(WorldToCanvas)),
                            Fill = Brushes.WhiteSmoke,
                            Stroke = ink, StrokeThickness = 0.7
                        });
                    }
                }
            }
        }
        else if (assembly.IsGypsum)
        {
            // Studs in plan are cross-section markers between continuous gypsum boards.
            var pitch = assembly.StudSpacing;
            var positions = WallAssemblyPlanRules.StudPositions(wall.Length, pitch);
            foreach (var position in positions.Take(3000))
            {
                if (InOpening(position)) continue;
                Stroke(position, left + 0.012, position, right - 0.012, Brushes.SlateGray, 1.5);
                if (_scale >= 210 && position + 0.04 <= wall.Length)
                    Stroke(position + 0.04, left + 0.012, position + 0.04, right - 0.012,
                        Brushes.SlateGray, 0.8);
            }
        }

        PlanCanvas.Children.Add(overlay);
    }
}
