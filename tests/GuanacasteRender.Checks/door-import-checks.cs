using Guanacaste3D.Core;

/// <summary>
/// Regression checks for importing door sizes from IFC, DXF, and OBJ files, and for
/// the leaf that an imported door carries inside walls of different thickness.
/// </summary>
static class DoorImportChecks
{
    /// <summary>Runs every door-import check in sequence.</summary>
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"guanacaste-doors-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CheckIfcImport(check, directory);
            CheckDxfImport(check, directory);
            CheckObjImport(check, directory);
            CheckLeafAndRejections(check);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Reads IfcDoor overall height and width from the instance attributes.</summary>
    private static void CheckIfcImport(Action<bool, string> check, string directory)
    {
        var path = Path.Combine(directory, "door.ifc");
        File.WriteAllText(path, """
            ISO-10303-21;
            DATA;
            #5=IFCLOCALPLACEMENT($,$);
            #12=IFCDOOR('0Y2s$Gh4P5Vfq0jEy7zEvV',#1,'Entry, door (v2)',$,$,#5,$,$,IFCPOSITIVELENGTHMEASURE(2.1),0.9,.DOOR.,$,$);
            ENDSEC;
            END-ISO-10303-21;
            """);
        var door = DoorFamilyImport.Read(path);
        check(door.SourceFormat == "IFC" && Math.Abs(door.Width - 0.9) < 1e-9 && Math.Abs(door.Height - 2.1) < 1e-9,
            "IFC import reads OverallWidth and OverallHeight from the IfcDoor attributes");

        var unset = Path.Combine(directory, "unset.ifc");
        File.WriteAllText(unset, "#3=IFCDOOR('g',#1,'Door',$,$,#5,$,$,$,$,$,$,$,$);");
        check(Rejects(() => DoorFamilyImport.Read(unset)), "An IfcDoor without declared sizes is rejected");
    }

    /// <summary>Reads a plan drawing in millimeters, taking the height from the caller.</summary>
    private static void CheckDxfImport(Action<bool, string> check, string directory)
    {
        var path = Path.Combine(directory, "door.dxf");
        File.WriteAllText(path, string.Join('\n', new[]
        {
            "0", "SECTION", "2", "HEADER",
            "9", "$INSUNITS", "70", "4",
            "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
            "0", "LINE", "8", "0", "10", "0", "20", "0", "11", "900", "21", "0",
            "0", "LINE", "8", "0", "10", "900", "20", "0", "11", "900", "21", "40",
            "0", "LINE", "8", "0", "10", "900", "20", "40", "11", "0", "21", "40",
            "0", "ENDSEC",
            "0", "EOF"
        }));
        var door = DoorFamilyImport.Read(path, planHeight: 2.2);
        check(door.SourceFormat == "DXF" && Math.Abs(door.Width - 0.9) < 1e-9 &&
              Math.Abs(door.Depth - 0.04) < 1e-9 && Math.Abs(door.Height - 2.2) < 1e-9,
            "DXF import converts $INSUNITS millimeters and takes width and leaf depth from the plan");
    }

    /// <summary>Reads an OBJ mesh of a door and orders its extents as height, width, and depth.</summary>
    private static void CheckObjImport(Action<bool, string> check, string directory)
    {
        var path = Path.Combine(directory, "door.obj");
        // A door modeled with Y as up: 0.04 thick along Z, 0.9 wide along X, 2.1 tall along Y.
        File.WriteAllText(path, string.Join('\n',
            "v 0 0 0", "v 0.9 0 0", "v 0 2.1 0", "v 0.9 2.1 0",
            "v 0 0 0.04", "v 0.9 0 0.04", "v 0 2.1 0.04", "v 0.9 2.1 0.04"));
        var door = DoorFamilyImport.Read(path);
        check(door.SourceFormat == "OBJ" && Math.Abs(door.Height - 2.1) < 1e-9 &&
              Math.Abs(door.Width - 0.9) < 1e-9 && Math.Abs(door.Depth - 0.04) < 1e-9,
            "OBJ import orders extents as height, width, and depth whatever axis is up");
        check(Rejects(() => DoorFamilyImport.Read(Path.Combine(directory, "door.skp"))),
            "Native SketchUp files are rejected with a clear message instead of guessed");
    }

    /// <summary>Verifies that leaf depth follows the wall and that invalid depths are rejected.</summary>
    private static void CheckLeafAndRejections(Action<bool, string> check)
    {
        var thin = new Wall(Guid.NewGuid(), Guid.NewGuid(), new Point2(0, 0), new Point2(5, 0), 0.10, 3);
        var thick = thin with { Thickness = 0.30 };
        var leafType = new OpeningType(Guid.NewGuid(), OpeningKind.Door, "Imported", "Leaf", 0.9, 2.1, 0)
        {
            Depth = 0.20
        };
        check(Math.Abs(WallGeometry.LeafDepth(leafType, thin) - 0.10) < 1e-9 &&
              Math.Abs(WallGeometry.LeafDepth(leafType, thick) - 0.20) < 1e-9,
            "A door leaf is capped at the thickness of the wall it sits in");
        check(WallGeometry.LeafDepth(leafType with { Depth = 0 }, thick) == 0,
            "A door without leaf depth stays a plain opening");

        var session = new DocumentSession(BuildingDocument.Create("Doors"));
        check(Rejects(() => session.AddOpeningType(OpeningKind.Door, "Imported", "Too deep", 0.9, 2.1, 0, depth: 2)),
            "A leaf deeper than one meter is rejected");
        var typeId = session.AddOpeningType(OpeningKind.Door, "Imported", "Leaf", 0.9, 2.1, 0, depth: 0.04);
        check(session.Document.OpeningTypes.Single(type => type.Id == typeId).Depth == 0.04,
            "Imported leaf depth is stored on the door type");
    }

    /// <summary>Returns true when the action throws the validation exception a user should see.</summary>
    private static bool Rejects(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }
}
