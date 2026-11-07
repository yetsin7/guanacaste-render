using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Regression checks for the wall, door and window catalog and how its constructions and styles are meshed.</summary>
static class CatalogChecks
{
    public static void Run(Action<bool, string> check)
    {
        var walls = ArchitecturalLibrary.WallTypes();
        check(walls.Count(type => type.Construction == WallConstruction.Gypsum) >= 6 &&
              walls.Count(type => type.Construction == WallConstruction.Glass) >= 3 &&
              walls.Count(type => type.Construction == WallConstruction.Block) >= 4,
            "The wall catalog offers gypsum partitions, glass partitions and concrete block walls");
        var openings = ArchitecturalLibrary.OpeningTypes();
        check(Enum.GetValues<DoorLeafStyle>().All(style => openings.Any(type => type.Kind == OpeningKind.Door && type.LeafStyle == style)) &&
              openings.Any(type => type.Kind == OpeningKind.Window && type.SecurityBars),
            "Every door leaf style and windows with security bars are in the catalog");
        check(openings.Select(type => type.Id).Distinct().Count() == openings.Count &&
              walls.Select(type => type.Id).Distinct().Count() == walls.Count,
            "Catalog types have distinct stable identifiers");

        SceneMesh MeshWall(string wallName, string? openingFamily = null)
        {
            var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Catalog"));
            var level = session.Document.Levels[0].Id;
            var type = session.Document.WallTypes.Single(candidate => candidate.Name == wallName);
            // A closed room so faces can tell inside from outside.
            var corners = new[] { new Point2(0, 0), new Point2(6, 0), new Point2(6, 4), new Point2(0, 4) };
            var first = Guid.Empty;
            for (var index = 0; index < 4; index++)
            {
                var id = session.AddWall(level, corners[index], corners[(index + 1) % 4], type.Thickness, 3, type.Id);
                if (index == 0) first = id;
            }
            if (openingFamily is not null)
            {
                var opening = session.Document.OpeningTypes.First(candidate => candidate.FamilyName == openingFamily);
                session.AddOpening(first, opening.Kind, offset: 3, openingTypeId: opening.Id);
            }
            return BuildingMesher.Build(session.Document);
        }

        int Count(SceneMesh mesh, SurfaceKind kind) => mesh.TriangleKinds.Count(candidate => candidate == kind);

        var brick = MeshWall("Exposed Brick 150 mm");
        check(Count(brick, SurfaceKind.WallBrick) > 0 && Count(brick, SurfaceKind.InteriorWall) > 0,
            "An exposed brick wall shows brick outside and plaster inside");
        var gypsum = MeshWall("Gypsum Partition 100 mm");
        check(Count(gypsum, SurfaceKind.WallBrick) == 0 && Count(gypsum, SurfaceKind.WallBlock) == 0 && Count(gypsum, SurfaceKind.InteriorWall) > 0,
            "A gypsum wall has smooth painted faces");
        var glass = MeshWall("Framed Glass Partition 100 mm");
        check(Count(glass, SurfaceKind.Glass) > 0 && Count(glass, SurfaceKind.Metal) > 0 && Count(glass, SurfaceKind.Wall) == 0,
            "A glass partition is a pane in an aluminium frame instead of a solid wall");
        check(Count(MeshWall("Concrete Block 200 mm"), SurfaceKind.WallBlock) > 0 &&
              Count(MeshWall("Reinforced Concrete 200 mm"), SurfaceKind.WallConcrete) > 0 &&
              Count(MeshWall("Stone Wall 400 mm"), SurfaceKind.WallStone) > 0 &&
              Count(MeshWall("Timber Clad Wall 200 mm"), SurfaceKind.WallWood) > 0 &&
              Count(MeshWall("Insulated Metal Panel 100 mm"), SurfaceKind.WallMetal) > 0,
            "Block, concrete, stone, timber and metal walls each have their own facade material");
        check(Count(MeshWall("Clay Brick 120 mm"), SurfaceKind.InteriorWall) == 0,
            "An interior brick partition is left exposed on both faces");

        var panelled = MeshWall("Rendered Block 150 mm", "Single Door");
        var plain = Count(panelled, SurfaceKind.DoorLeaf);
        var styles = new[] { "Flush Door", "Six-Panel Door", "Louvered Door" }
            .Select(family => Count(MeshWall("Rendered Block 150 mm", family), SurfaceKind.DoorLeaf)).ToArray();
        check(styles.Distinct().Count() == 3 && styles.All(count => count > 0 && count != plain),
            "Flush, six-panel and louvered doors are each built differently from a panelled door");
        var frameless = MeshWall("Rendered Block 150 mm", "Frameless Glass Door");
        check(Count(frameless, SurfaceKind.DoorLeaf) == 0 && Count(frameless, SurfaceKind.Glass) > 0,
            "A frameless glass door is all glass");
        var barred = MeshWall("Rendered Block 150 mm", openings.First(type => type.SecurityBars).FamilyName);
        var unbarred = MeshWall("Rendered Block 150 mm", openings.First(type => type.Kind == OpeningKind.Window && !type.SecurityBars).FamilyName);
        check(Count(barred, SurfaceKind.OpeningFrame) > Count(unbarred, SurfaceKind.OpeningFrame) + 24,
            "A window with security bars carries a grille");
        // The first wall runs along y = 0 with the room at y > 0, so its outside is toward negative y.
        // The sill also projects outward, so count frame triangles beyond the 75 mm outer face rather than the outermost one.
        int Outside(SceneMesh mesh) => Enumerable.Range(0, mesh.TriangleCount).Count(triangle =>
            mesh.TriangleKinds[triangle] == SurfaceKind.OpeningFrame &&
            Enumerable.Range(0, 3).All(corner => mesh.Positions[mesh.Indices[triangle * 3 + corner]].Y < -0.08f));
        check(Outside(barred) > Outside(unbarred) + 24, "The grille stands on the outside face of the wall");
    }
}
