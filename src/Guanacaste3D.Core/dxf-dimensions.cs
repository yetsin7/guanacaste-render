using System.Text;

namespace Guanacaste3D.Core;

public static partial class DxfExporter
{
    /// <summary>
    /// Exports current associative measurements as portable line and text graphics.
    /// DXF R12 output is a drawing snapshot; model constraints remain in the G3 project.
    /// </summary>
    private static void WriteDimensions(StringBuilder output, BuildingDocument document, Level level, int index)
    {
        var layer = LayerName(index, "DIMENSION");
        foreach (var dimension in document.Dimensions.Where(item => item.LevelId == level.Id).OrderBy(item => item.Id))
        {
            if (PlanDimensions.Resolve(document, dimension) is not { } measurement) continue;
            var length = measurement.From.DistanceTo(measurement.To);
            if (length < 1e-9) continue;
            var axisX = (measurement.To.X - measurement.From.X) / length;
            var axisY = (measurement.To.Y - measurement.From.Y) / length;
            WriteLine(output, measurement.From, measurement.To, level.Elevation, layer);
            foreach (var point in new[] { measurement.From, measurement.To })
            {
                // Oblique architectural ticks stay legible without relying on CAD dimension styles.
                var tickX = (axisX - axisY) * 0.07;
                var tickY = (axisY + axisX) * 0.07;
                WriteLine(output, new(point.X - tickX, point.Y - tickY),
                    new(point.X + tickX, point.Y + tickY), level.Elevation, layer);
            }
            var label = new Point2((measurement.From.X + measurement.To.X) / 2,
                (measurement.From.Y + measurement.To.Y) / 2 + 0.2);
            WriteText(output, label, level.Elevation, layer, measurement.IsAngle ? Number(measurement.Value) + "%%d" : document.Units.Length == LengthUnit.Meters ? Number(measurement.Value) : document.Units.FormatLength(measurement.Value));
        }
    }
}
