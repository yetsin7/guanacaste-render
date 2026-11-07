using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Regression checks for site planting (procedural trees and shrubs) and the procedural cloud layer.</summary>
static class PlantingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create("Planting"));
        var level = session.Document.Levels[0].Id;
        var species = ArchitecturalLibrary.PresetsFor(StructuralElementKind.Planting).ToArray();
        check(species.Select(preset => preset.Style).Distinct().Count() == 5 &&
              species.Any(preset => preset.Style == ElementStyle.Guanacaste),
            "The planting catalog offers five growth forms, the guanacaste tree among them");

        var ids = species.Select((preset, index) =>
            session.AddElementFromPreset(level, preset, new Point2(index * 30, 0), new Point2(index * 30, 0))).ToArray();
        var plants = ids.Select(id => session.Document.StructuralElements.Single(element => element.Id == id)).ToArray();
        var everyBoundOk = true;
        var everySmooth = true;
        foreach (var plant in plants)
        {
            var document = BuildingDocument.Create("One");
            document.StructuralElements.Add(plant with { LevelId = document.Levels[0].Id });
            var mesh = BuildingMesher.Build(document);
            var (min, max) = mesh.Bounds();
            var spread = Math.Max(max.X - min.X, max.Y - min.Y);
            everyBoundOk &= mesh.TriangleCount > 100 && min.Z >= -0.01f && max.Z <= plant.Height * 1.12 &&
                            spread <= plant.Width * 1.3 && spread >= plant.Width * 0.6;
            everySmooth &= mesh.Normals is { } normals && normals.Count == mesh.Positions.Count &&
                           normals.Any(normal => normal != Vector3.Zero);
        }
        check(everyBoundOk, "Every species stays within its height and canopy spread and stands on the level");
        check(everySmooth, "Tree meshes carry smooth vertex normals aligned with their vertices");

        var again = BuildingMesher.Build(session.Document);
        var first = BuildingMesher.Build(session.Document);
        check(first.Positions.SequenceEqual(again.Positions),
            "A planting regenerates identically from its id");
        var twin = plants[0] with { Id = Guid.NewGuid() };
        var twinDocument = BuildingDocument.Create("Twin");
        twinDocument.StructuralElements.Add(plants[0] with { LevelId = twinDocument.Levels[0].Id });
        twinDocument.StructuralElements.Add(twin with { LevelId = twinDocument.Levels[0].Id });
        var twinMesh = BuildingMesher.Build(twinDocument);
        var half = twinMesh.Positions.Count / 2;
        check(!twinMesh.Positions.Take(half).SequenceEqual(twinMesh.Positions.Skip(half)),
            "Two trees of the same species at the same place still differ, because each id seeds its own shape");

        check(ElementGeometry.Parts(plants[1]).Any(part => part.Kind == SurfaceKind.Foliage) &&
              ElementGeometry.Parts(plants[1]).Any(part => part.Kind == SurfaceKind.Bark),
            "A tree offers trunk and canopy volumes for picking, plans and exchange");

        var rejected = false;
        try { session.AddElementFromPreset(level, species[0] with { Width = 0.05 }, new Point2(0, 50), new Point2(0, 50)); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "A planting with a canopy narrower than 20 cm is rejected");

        var path = Path.Combine(Path.GetTempPath(), $"planting-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var reloaded = ProjectFile.Load(path);
            check(reloaded.StructuralElements.Count(element => element.Kind == StructuralElementKind.Planting) == 5 &&
                  reloaded.StructuralElements.Any(element => element.Style == ElementStyle.Palm),
                "Plantings and their growth form survive a project round trip");
        }
        finally { File.Delete(path); }

        var scene = new RenderSceneBuilder().AddBuilding(BuildingMesher.Build(session.Document, includeGround: true)).Build();
        check(scene.Normals is { } sceneNormals && sceneNormals.Length == scene.Positions.Length &&
              scene.Materials.Any(material => material.Pattern == SurfacePattern.Foliage) &&
              scene.Materials.Any(material => material.Pattern == SurfacePattern.Bark),
            "The render scene receives smooth foliage normals and the foliage and bark materials");

        var clear = new CloudLayer(0);
        var overcast = new CloudLayer(1);
        var scattered = new CloudLayer(0.3f);
        var directions = Enumerable.Range(0, 400).Select(index =>
        {
            var azimuth = index * 2.399963f;
            var elevation = 0.25f + 0.7f * (index % 20) / 20f;
            return Vector3.Normalize(new Vector3(MathF.Cos(azimuth), MathF.Sin(azimuth), elevation));
        }).ToArray();
        float Cover(CloudLayer layer) => directions.Average(direction => layer.Sample(direction).Opacity);
        check(Cover(clear) == 0 && Cover(scattered) is > 0.03f and < 0.5f && Cover(overcast) > 0.75f,
            "Cloud cover goes from a clear sky through scattered cumulus to nearly overcast");
        check(overcast.Sample(new Vector3(1, 0, -0.2f)).Opacity == 0,
            "There are no clouds below the horizon");
    }
}
