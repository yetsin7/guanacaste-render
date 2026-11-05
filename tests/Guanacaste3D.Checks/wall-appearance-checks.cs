using System.Text.Json;
using System.Text.Json.Nodes;
using Guanacaste3D.Core;

/// <summary>Regression checks for wall drafting appearance persistence and backward compatibility.</summary>
static class WallAppearanceChecks
{
    /// <summary>Verifies that pre-appearance schema-eight projects receive safe drafting defaults.</summary>
    public static void Run(Action<bool, string> check)
    {
        var document = BuildingDocument.Create();
        var root = JsonNode.Parse(JsonSerializer.Serialize(document))!.AsObject();
        foreach (var wallTypeNode in root["WallTypes"]!.AsArray())
        {
            var wallType = wallTypeNode!.AsObject();
            wallType.Remove("PlanFillColor");
            wallType.Remove("PlanFillPattern");
            wallType.Remove("SectionFillColor");
            wallType.Remove("SectionFillPattern");
        }

        var filename = Path.Combine(Path.GetTempPath(), $"guanacaste-appearance-{Guid.NewGuid():N}.g3");
        try
        {
            File.WriteAllText(filename, "G3-PROJECT/1\n" + root.ToJsonString());
            var loaded = ProjectFile.Load(filename);
            check(loaded.WallTypes.All(type => type.PlanFillColor == "#59636B" &&
                    type.PlanFillPattern == WallFillPattern.Solid &&
                    type.SectionFillColor == "#87928B" &&
                    type.SectionFillPattern == WallFillPattern.Diagonal),
                "Schema-eight wall types without appearance fields receive coordinated defaults");
        }
        finally
        {
            if (File.Exists(filename)) File.Delete(filename);
        }
    }
}
