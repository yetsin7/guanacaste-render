using System.Globalization;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks the site imports on the breeze-block house: survey points to terrain, point clouds, gbXML spaces and kept images and PDFs.</summary>
static class SiteImportChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-9) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    public static void Run(Action<bool, string> check)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Points(check);
            Gbxml(check);
            Files(check);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void Points(Action<bool, string> check)
    {
        var text = "x,y,z\n# comentario\n0,0,10.5\n10;0;11\n10 10 12\n0\t10\t11.5\nrota,1,2\n5 5\n\n3,4,5,255,0,0\n";
        var read = SurveyPointReader.Parse(text);
        check(read.Points.Count == 5 && read.Skipped == 2 && read.Points[0] == (0, 0, 10.5) && read.Points[4] == (3, 4, 5),
            $"Survey points are read with commas, semicolons, tabs or spaces; a header and comments are ignored, broken lines are counted ({read.Points.Count} points, {read.Skipped} skipped), and colours after z are ignored");
        check(SurveyPointReader.Parse("1 2 NaN\n1 2 3e99\n2 3 4").Points.Count == 1 && SurveyPointReader.Parse(null).Points.Count == 0, "Numbers that are not finite or are absurd are not points");

        // Random terrain: the same hill sampled at random places must come back as the same hill.
        var random = new Random(20261107);
        double Hill(double x, double y) => 100 + 0.05 * x + 0.02 * y;
        var survey = Enumerable.Range(0, 400).Select(_ => { var x = random.NextDouble() * 60; var y = random.NextDouble() * 40; return (x, y, Hill(x, y)); }).ToList();
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0];
        var (terrainId, kept) = session.AddTerrainFromPoints(level.Id, survey, "Lote");
        var terrain = session.Document.Floors.Single(floor => floor.Id == terrainId);
        var surface = new TopoSurface(terrain);
        var worst = 0.0;
        for (var probe = 0; probe < 50; probe++)
        {
            var x = 5 + random.NextDouble() * 50;
            var y = 5 + random.NextDouble() * 30;
            worst = Math.Max(worst, Math.Abs(surface.ElevationAt(x, y) + level.Elevation - Hill(x, y)));
        }
        check(terrain.IsToposolid && kept == 400 && worst < 0.05, $"400 random survey points make a terrain that follows the same slope to within {worst * 100:0.0} cm");

        var dense = Enumerable.Range(0, 5000).Select(index => ((double)(index % 100), (double)(index / 100), 50.0 + index % 7)).ToList();
        var (_, thinned) = session.AddTerrainFromPoints(level.Id, dense, "Denso");
        check(thinned <= SurveyPointReader.MaximumTerrainPoints && thinned > 100, $"5000 points are thinned to {thinned}, spread over the whole site");
        check(Throws(() => session.AddTerrainFromPoints(level.Id, [(0, 0, 1), (1, 1, 1)])) && Throws(() => session.AddTerrainFromPoints(level.Id, [(0, 0, 1), (0.01, 0, 2), (0.02, 0.01, 3)])) &&
              Throws(() => session.AddTerrainFromPoints(Guid.NewGuid(), survey)), "Fewer than three points, points on top of each other or an unknown level are refused");

        var cloud = session.AddPointCloud(level.Id, "Escaneo", survey);
        check(session.Document.PointClouds.Single(item => item.Id == cloud).Points.Count == 400 && Throws(() => session.AddPointCloud(level.Id, "", survey)) &&
              Throws(() => session.AddPointCloud(level.Id, "Vacío", [])), "A point cloud is kept with its points; a nameless or empty one is refused");
        session.RemovePointCloud(cloud);
        check(session.Document.PointClouds.Count == 0, "A cloud can be removed");
    }

    private static void Gbxml(Action<bool, string> check)
    {
        const string xml = """
            <gbXML xmlns="http://www.gbxml.org/schema" lengthUnit="Feet" temperatureUnit="F">
              <Campus><Building>
                <Space id="sp-1"><Name>Sala</Name>
                  <ShellGeometry><ClosedShell>
                    <PolyLoop><CartesianPoint><Coordinate>0</Coordinate><Coordinate>0</Coordinate><Coordinate>0</Coordinate></CartesianPoint>
                              <CartesianPoint><Coordinate>20</Coordinate><Coordinate>0</Coordinate><Coordinate>0</Coordinate></CartesianPoint>
                              <CartesianPoint><Coordinate>20</Coordinate><Coordinate>10</Coordinate><Coordinate>0</Coordinate></CartesianPoint>
                              <CartesianPoint><Coordinate>0</Coordinate><Coordinate>10</Coordinate><Coordinate>0</Coordinate></CartesianPoint></PolyLoop>
                    <PolyLoop><CartesianPoint><Coordinate>0</Coordinate><Coordinate>0</Coordinate><Coordinate>9</Coordinate></CartesianPoint>
                              <CartesianPoint><Coordinate>20</Coordinate><Coordinate>0</Coordinate><Coordinate>9</Coordinate></CartesianPoint>
                              <CartesianPoint><Coordinate>20</Coordinate><Coordinate>10</Coordinate><Coordinate>9</Coordinate></CartesianPoint></PolyLoop>
                  </ClosedShell></ShellGeometry></Space>
                <Space id="sp-2"><ShellGeometry><ClosedShell>
                    <PolyLoop><CartesianPoint><Coordinate>30</Coordinate><Coordinate>0</Coordinate><Coordinate>0</Coordinate></CartesianPoint>
                              <CartesianPoint><Coordinate>40</Coordinate><Coordinate>0</Coordinate><Coordinate>0</Coordinate></CartesianPoint>
                              <CartesianPoint><Coordinate>40</Coordinate><Coordinate>5</Coordinate><Coordinate>0</Coordinate></CartesianPoint></PolyLoop>
                  </ClosedShell></ShellGeometry></Space>
              </Building></Campus>
            </gbXML>
            """;
        var spaces = GbxmlReader.Read(xml);
        check(spaces.Count == 2 && spaces[0].Name == "Sala" && spaces[1].Name == "sp-2" && Near(spaces[0].Outline.Max(p => p.X), 20 * 0.3048, 1e-9),
            "gbXML spaces are read with their names (or ids), their floor outline, and the file's length unit converted to meters");
        check(Throws(() => GbxmlReader.Read("<html/>")) && Throws(() => GbxmlReader.Read("no es xml")) && GbxmlReader.Read("<gbXML><Campus/></gbXML>").Count == 0,
            "A file that is not gbXML is refused, and one without spaces has none");

        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        var rooms = session.Document.Rooms.Count;
        var steps = session.UndoNames.Count;
        var made = session.ImportGbxmlSpaces(level, spaces);
        check(made == 2 && session.Document.Rooms.Count == rooms + 2 && session.UndoNames.Count == steps + 1 && session.Document.Rooms.Any(room => room.Name == "Sala"),
            "The spaces become rooms in one undoable step");
        check(Throws(() => session.ImportGbxmlSpaces(level, [])) && Throws(() => session.ImportGbxmlSpaces(Guid.NewGuid(), spaces)), "No spaces, or an unknown level, are refused");
    }

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");

    private static void Files(Action<bool, string> check)
    {
        var session = new DocumentSession(BreezeBlockHouseShowcase.Create());
        var level = session.Document.Levels[0].Id;
        var image = session.AddReferenceFile(ReferenceKind.Image, "plano.png", Png(), 800, 600);
        var pdf = session.AddReferenceFile(ReferenceKind.Pdf, "memoria.pdf", Pdf());
        check(session.Document.References.Count == 2 && session.Document.References.Single(item => item.Id == pdf).PixelWidth == 0,
            "An image and a PDF are kept inside the project");
        check(Throws(() => session.AddReferenceFile(ReferenceKind.Image, "mala.png", Pdf(), 10, 10)) && Throws(() => session.AddReferenceFile(ReferenceKind.Pdf, "mala.pdf", Png())) &&
              Throws(() => session.AddReferenceFile(ReferenceKind.Image, "sin-tamaño.png", Png(), 0, 0)) && Throws(() => session.AddReferenceFile(ReferenceKind.Image, "", Png(), 5, 5)) &&
              Throws(() => session.AddReferenceFile(ReferenceKind.Pdf, "grande.pdf", Pdf().Concat(new byte[References_MaximumBytes()]).ToArray())),
            "A file that is not what it says, an image without a size, no name, or a file over 4 MB is refused");

        var steps = session.UndoNames.Count;
        var underlay = session.PlaceImageUnderlay(level, image, new Point2(2, 3), 20);
        var placed = session.Document.Underlays.Single(item => item.Id == underlay);
        check(session.UndoNames.Count == steps + 1 && Near(placed.Width, 20) && placed.Opacity == 0.6, "An image is laid in the plan of a level, 20 m wide");
        session.SetImageUnderlay(underlay, new Point2(1, 1), 12.5, 30, 0.3);
        check(session.Document.Underlays.Single().Width == 12.5 && session.Document.Underlays.Single().RotationDegrees == 30, "Its place, width, turn and opacity can change");
        check(Throws(() => session.SetImageUnderlay(underlay, new Point2(0, 0), 0.01, 0, 0.5)) && Throws(() => session.SetImageUnderlay(underlay, new Point2(0, 0), 5, 0, 0)) &&
              Throws(() => session.SetImageUnderlay(underlay, new Point2(0, 0), 5, 999, 0.5)) && Throws(() => session.PlaceImageUnderlay(level, pdf, new Point2(0, 0), 5)),
            "A width or opacity out of range, a turn past 360° and a PDF laid as an image are refused");

        var file = Path.Combine(Path.GetTempPath(), $"refs-{Guid.NewGuid():N}.g3d");
        ProjectFile.Save(file, session.Document);
        var reopened = ProjectFile.Load(file);
        File.Delete(file);
        check(reopened.References.Count == 2 && reopened.References.Single(item => item.Id == image).Data.SequenceEqual(Png()) && reopened.Underlays.Count == 1,
            "The kept files travel inside the saved project, byte for byte");

        session.RemoveReferenceFile(image);
        check(session.Document.References.Count == 1 && session.Document.Underlays.Count == 0 && Throws(() => session.RemoveReferenceFile(image)),
            "Removing an image takes its placements with it");
        session.Undo();
        check(session.Document.Underlays.Count == 1, "Undo brings the image and its placement back");
    }

    private static int References_MaximumBytes() => ReferenceFiles.MaximumFileBytes;
}
