using System.Text.Json;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>
/// Regression checks for Estructura elements: default sections, footprints, beam-system layout,
/// joint connections, moves with undo, and serialization that keeps derived geometry out of files.
/// </summary>
static class StructuralChecks
{
    /// <summary>Runs every structural check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var levelId = session.Document.Levels[0].Id;

        var beamId = session.AddStructuralElement(levelId, StructuralElementKind.Beam, "Viga", new Point2(0, 0), new Point2(4, 0));
        var beam = session.Document.StructuralElements.Single(element => element.Id == beamId);
        check(beam.Offset == -0.5 && beam.Height == 0.5 && beam.Width == 0.3,
            "Beams take the standard section and hang below the level");

        var footprint = beam.Footprint;
        check(footprint.Count == 4 && footprint.Min(point => point.Y) == -0.15 && footprint.Max(point => point.Y) == 0.15,
            "Line footprint spans the section width on both sides of the axis");

        var columnId = session.AddColumn(levelId, new Point2(20, 20));
        check(session.Document.Columns.Any(column => column.Id == columnId),
            "Column tool data is available to structural joints");

        var beamSystemCount = session.AddBeamSystem(levelId, "Viga", new Point2(0, 10), new Point2(6, 13), 1.2);
        var beamSystem = session.Document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Beam && element.Start.Y >= 10).ToArray();
        check(beamSystemCount == 4 && beamSystem.Length == 4 && beamSystem.All(element => element.Start.X == 0 && element.End.X == 6),
            "Beam system runs along the longer side with edge beams and even spacing");

        var connectionStart = session.Document.StructuralElements.Count(element => element.Kind == StructuralElementKind.Connection);
        session.AddStructuralElement(levelId, StructuralElementKind.Beam, "Viga", new Point2(4, 0), new Point2(4, 3));
        var created = session.AddConnectionsAtJoints(levelId, "Conexión");
        check(created > 0 && session.Document.StructuralElements.Count(element => element.Kind == StructuralElementKind.Connection) == connectionStart + created,
            "Connections are created at member joints and column joints");
        check(session.AddConnectionsAtJoints(levelId, "Conexión") == 0,
            "Repeated connection automation does not duplicate joints");

        session.MoveStructuralElement(beamId, 1, 2);
        var moved = session.Document.StructuralElements.Single(element => element.Id == beamId);
        check(moved.Start == new Point2(1, 2) && moved.End == new Point2(5, 2) && moved.Width == beam.Width,
            "Moving an element translates its ends and keeps its section");
        session.Undo();
        check(session.Document.StructuralElements.Single(element => element.Id == beamId).Start == new Point2(0, 0),
            "Undo restores the element's previous position");

        session.RemoveStructuralElement(beamId);
        check(session.Document.StructuralElements.All(element => element.Id != beamId),
            "Removing an element deletes only that element");

        var json = JsonSerializer.Serialize(beam);
        check(!json.Contains("Footprint", StringComparison.Ordinal) && !json.Contains("HasSolid", StringComparison.Ordinal),
            "Derived geometry is not written to project files");

        RunOpeningChecks(check);
        RunConcaveOpeningChecks(check);
        RunGroupAndTextChecks(check);
        RunDormerChecks(check);
    }

    /// <summary>Rejects openings crossing a concave notch even when all four corners are inside the slab.</summary>
    private static void RunConcaveOpeningChecks(Action<bool, string> check)
    {
        var boundary = new Point2[]
        {
            new(0, 0), new(10, 0), new(10, 10), new(6, 10),
            new(6, 4), new(4, 4), new(4, 10), new(0, 10)
        };
        var crossing = new PlanRectangle(3, 3, 7, 8);
        var inside = new PlanRectangle(1, 1, 3, 8);
        var touching = new PlanRectangle(0, 0, 3, 3);
        check(SlabOpenings.Corners(crossing).All(point => SlabOpenings.ContainsPoint(boundary, point)) &&
              !SlabOpenings.ContainsRectangle(boundary, crossing) &&
              !SlabOpenings.ContainsRectangle(boundary.Reverse().ToArray(), crossing),
            "A rectangular opening cannot cross a concave notch between its corners");
        check(SlabOpenings.ContainsRectangle(boundary, inside) && SlabOpenings.ContainsRectangle(boundary, touching),
            "A contained opening remains valid, including one touching the slab boundary");

        var session = new DocumentSession(BuildingDocument.Create());
        var level = session.Document.Levels[0].Id;
        session.AddFloor(level, boundary);
        var rejected = session.AddOpening(level, StructuralElementKind.Opening, "Opening", new(3, 3), new(7, 8));
        check(rejected is null && session.Document.StructuralElements.Count == 0,
            "An invalid concave slab opening is rejected without changing the document");
    }

    /// <summary>A dormer must sit fully on a roof slab, cut that roof, and stand on the roof's raised top face.</summary>
    private static void RunDormerChecks(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var levelId = session.Document.Levels[0].Id;
        var boundary = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) };
        session.AddFloor(levelId, boundary, 0.20, kind: FloorKind.Roof, elevationOffset: 3.0);

        var offRoof = session.AddDormer(levelId, "Buhardilla", new Point2(4, 4), new Point2(12, 6));
        check(offRoof is null && session.Document.StructuralElements.Count == 0,
            "A dormer that reaches past the roof is rejected without changing the project");

        var dormerId = session.AddDormer(levelId, "Buhardilla", new Point2(4, 4), new Point2(6, 6));
        check(dormerId is Guid, "A dormer fully inside a roof slab is accepted");
        var dormer = session.Document.StructuralElements.Single(element => element.Id == dormerId!.Value);
        check(dormer.Offset == 3.0 && dormer.Height == 2.40,
            "A dormer stands on the roof's top face with its standard body height");

        var volume = SignedVolume(BuildingMesher.Build(session.Document));
        // Roof: (100 - 4) m² with 0.20 m thickness. Body: 4 m² with 2.40 m height above the roof top.
        check(Math.Abs(volume - ((100 - 4) * 0.20 + 4 * 2.40)) < 1e-3,
            "The roof is cut under the dormer and the dormer body closes its own volume");
    }

    /// <summary>Openings must fit inside a floor, and the meshed slab must lose exactly the opening's volume.</summary>
    private static void RunOpeningChecks(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var levelId = session.Document.Levels[0].Id;
        session.AddFloor(levelId, [new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10)], 0.20);

        var rejected = session.AddOpening(levelId, StructuralElementKind.Opening, "Abertura", new Point2(9, 9), new Point2(12, 12));
        check(rejected is null && session.Document.StructuralElements.Count == 0,
            "An opening that leaves the floor is rejected without changing the project");

        var accepted = session.AddOpening(levelId, StructuralElementKind.Opening, "Abertura", new Point2(4, 4), new Point2(6, 6));
        check(accepted is Guid, "An opening inside the floor is accepted");

        var mesh = BuildingMesher.Build(session.Document);
        var volume = SignedVolume(mesh);
        check(Math.Abs(volume - (100 - 4) * 0.20) < 1e-3,
            "The slab loses exactly the opening's volume and its faces stay consistently oriented");

        var shaftSession = new DocumentSession(BuildingDocument.Create());
        var lowerId = shaftSession.Document.Levels[0].Id;
        var upperId = shaftSession.AddLevel();
        shaftSession.AddFloor(upperId, [new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10)], 0.20);
        shaftSession.AddFloor(lowerId, [new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10)], 0.20);
        shaftSession.AddOpening(lowerId, StructuralElementKind.Shaft, "Hueco", new Point2(1, 1), new Point2(3, 3));
        var shaftVolume = SignedVolume(BuildingMesher.Build(shaftSession.Document));
        check(Math.Abs(shaftVolume - 2 * (100 - 4) * 0.20) < 1e-3,
            "A shaft cuts every floor at or above its level");
    }

    /// <summary>Model text keeps its string without solid geometry, and group members move and delete together.</summary>
    private static void RunGroupAndTextChecks(Action<bool, string> check)
    {
        var session = new DocumentSession(BuildingDocument.Create());
        var levelId = session.Document.Levels[0].Id;

        var textId = session.AddModelText(levelId, new Point2(30, 30), "  Cota libre  ");
        var text = session.Document.StructuralElements.Single(element => element.Id == textId);
        check(text.Name == "Cota libre" && !text.HasSolid, "Model text stores trimmed text and adds no solid");

        var first = session.AddStructuralElement(levelId, StructuralElementKind.Beam, "Viga", new Point2(0, 0), new Point2(4, 0));
        var second = session.AddStructuralElement(levelId, StructuralElementKind.Beam, "Viga", new Point2(0, 3), new Point2(4, 3));
        var outside = session.AddStructuralElement(levelId, StructuralElementKind.Beam, "Viga", new Point2(20, 20), new Point2(24, 20));
        var grouped = session.AddStructuralGroup(levelId, new Point2(-1, -1), new Point2(5, 4));
        check(grouped == 2 && session.Document.StructuralElements.Single(element => element.Id == outside).GroupId is null,
            "A group takes only the elements that lie fully inside its rectangle");

        session.MoveStructuralElement(first, 1, 0);
        check(session.Document.StructuralElements.Single(element => element.Id == second).Start == new Point2(1, 3),
            "Moving one group member moves the whole group");

        session.RemoveStructuralElement(first);
        check(session.Document.StructuralElements.All(element => element.Id != second) &&
            session.Document.StructuralElements.Any(element => element.Id == outside),
            "Deleting one group member deletes the group and nothing outside it");
    }

    /// <summary>Divergence-theorem volume of a closed mesh; correct only when every triangle faces outward.</summary>
    private static double SignedVolume(SceneMesh mesh)
    {
        var total = 0d;
        for (var index = 0; index < mesh.Indices.Count; index += 3)
        {
            var a = mesh.Positions[mesh.Indices[index]];
            var b = mesh.Positions[mesh.Indices[index + 1]];
            var c = mesh.Positions[mesh.Indices[index + 2]];
            total += System.Numerics.Vector3.Dot(a, System.Numerics.Vector3.Cross(b, c));
        }
        return total / 6;
    }
}
