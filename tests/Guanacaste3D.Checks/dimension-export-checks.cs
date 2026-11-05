using Guanacaste3D.Core;

/// <summary>Checks exported annotations against the model rather than cached screen labels.</summary>
internal static class DimensionExportChecks
{
    /// <summary>Parses DXF entities to verify geometry, updated values, level routing and deletion.</summary>
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels.Single(level => level.Role == LevelRole.BaseRoof).Id;
        var wall = session.AddWall(level, new(0, 0), new(12, 0));
        var door = session.AddOpening(wall, OpeningKind.Door, 3.45);
        var dimension = PlanDimensions.ForSelection(session.Document, door)
            .Single(item => item.Kind == PlanDimensionKind.OpeningStart);
        session.AddDimension(dimension);
        var layer = DxfExporter.LayerName(1, "DIMENSION");
        var entities = DimensionEntities(DxfExporter.ToAscii(session.Document), layer);
        check(entities.Count(item => item[0] == "LINE") == 3 && entities.Single(item => item[0] == "TEXT")[1] == "3",
            "DXF exports a measurement line, two ticks and the resolved three-meter value");
        check(entities.All(item => item[30] == "3"), "DXF dimension geometry retains its level elevation");
        session.EditDimension(dimension, 5);
        entities = DimensionEntities(DxfExporter.ToAscii(session.Document), layer);
        check(entities.Single(item => item[0] == "TEXT")[1] == "5" && entities.First(item => item[0] == "LINE")[11] == "5",
            "DXF recomputes both annotation geometry and text after an associative edit");
        session.RemoveOpening(door);
        check(DimensionEntities(DxfExporter.ToAscii(session.Document), layer).Count == 0,
            "DXF does not retain orphan annotation graphics after owner deletion");
    }

    /// <summary>Reads LINE and TEXT group codes on the requested annotation layer.</summary>
    private static List<Dictionary<int, string>> DimensionEntities(string ascii, string layer)
    {
        var lines = ascii.Split("\r\n");
        var entities = new List<Dictionary<int, string>>();
        Dictionary<int, string>? entity = null;
        for (var index = 0; index + 1 < lines.Length; index += 2)
        {
            var code = int.Parse(lines[index], System.Globalization.CultureInfo.InvariantCulture);
            var value = lines[index + 1];
            if (code == 0)
            {
                if (entity is not null && entity.GetValueOrDefault(8) == layer) entities.Add(entity);
                entity = value is "LINE" or "TEXT" ? new() { [0] = value } : null;
            }
            else if (entity is not null) entity[code] = value;
        }
        return entities;
    }
}
