using System.Text.RegularExpressions;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Interop;

/// <summary>IFC4 export checks: GlobalId encoding, STEP structure, IFC4 attribute arity, and content.</summary>
static class IfcChecks
{
    // Number of explicit attributes per entity in the IFC4 ADD2 TC1 schema.
    private static readonly Dictionary<string, int> Arity = new(StringComparer.Ordinal)
    {
        ["IFCSIUNIT"] = 4, ["IFCUNITASSIGNMENT"] = 1, ["IFCCARTESIANPOINT"] = 1, ["IFCDIRECTION"] = 1,
        ["IFCAXIS2PLACEMENT3D"] = 3, ["IFCAXIS2PLACEMENT2D"] = 2, ["IFCLOCALPLACEMENT"] = 2,
        ["IFCGEOMETRICREPRESENTATIONCONTEXT"] = 6, ["IFCGEOMETRICREPRESENTATIONSUBCONTEXT"] = 10,
        ["IFCPROJECT"] = 9, ["IFCSITE"] = 14, ["IFCBUILDING"] = 12, ["IFCBUILDINGSTOREY"] = 10,
        ["IFCRELAGGREGATES"] = 6, ["IFCRELCONTAINEDINSPATIALSTRUCTURE"] = 6, ["IFCRELASSOCIATESMATERIAL"] = 6,
        ["IFCRELVOIDSELEMENT"] = 6, ["IFCRELFILLSELEMENT"] = 6, ["IFCMATERIAL"] = 3,
        ["IFCWALL"] = 9, ["IFCSLAB"] = 9, ["IFCOPENINGELEMENT"] = 9, ["IFCDOOR"] = 13, ["IFCWINDOW"] = 13,
        ["IFCSPACE"] = 11, ["IFCRECTANGLEPROFILEDEF"] = 5, ["IFCARBITRARYCLOSEDPROFILEDEF"] = 3,
        ["IFCARBITRARYPROFILEDEFWITHVOIDS"] = 4,
        ["IFCPOLYLINE"] = 1, ["IFCEXTRUDEDAREASOLID"] = 4, ["IFCSHAPEREPRESENTATION"] = 4,
        ["IFCPRODUCTDEFINITIONSHAPE"] = 3
    };

    public static void Run(Action<bool, string> check)
    {
        check(IfcExporter.ToGlobalId(Guid.Empty) == "0000000000000000000000" &&
              IfcExporter.ToGlobalId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")) == "3$$$$$$$$$$$$$$$$$$$$$",
            "IFC GlobalId encodes the 128-bit extremes");
        var sampleGuid = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
        check(DecodeGlobalId(IfcExporter.ToGlobalId(sampleGuid)) == sampleGuid,
            "IFC GlobalId round-trips through the base-64 decoder");
        check(IfcExporter.Real(2) == "2." && IfcExporter.Real(-0.2) == "-0.2" && IfcExporter.Real(0) == "0.",
            "IFC reals always carry a decimal point");
        var preciseValues = new[] { 1e-13, -1e-13, 0.00000000000042, 1.2345678901234567, 1234567890123.125 };
        check(preciseValues.All(value =>
                double.Parse(IfcExporter.Real(value), System.Globalization.CultureInfo.InvariantCulture) == value) &&
              IfcExporter.Real(1e-13).Contains(".E-", StringComparison.Ordinal),
            "IFC STEP reals preserve tiny coordinates and full double precision through scientific notation");
        check(IfcExporter.Text("Sala 'A' ñ\\") == @"'Sala ''A'' \X2\00F1\X0\\\'",
            "IFC strings escape quotes, backslashes and non-ASCII");
        check(IfcExporter.Text("Árbol 🌳") ==
              @"'\X2\00C1\X0\rbol \X4\0001F333\X0\'",
            "IFC strings encode supplementary Unicode scalars with STEP X4, not surrogate halves");
        var rejectedSurrogate = false;
        try { IfcExporter.Text("\uD83C"); }
        catch (ArgumentException) { rejectedSurrogate = true; }
        check(rejectedSurrogate, "IFC strings reject invalid unpaired UTF-16 surrogates");


        var house = SampleProjects.CreateDemonstrationHouse();
        var stamp = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        var step = IfcExporter.ToStep(house, stamp);
        check(step.StartsWith("ISO-10303-21;\nHEADER;", StringComparison.Ordinal) &&
              step.Contains("FILE_SCHEMA(('IFC4'));", StringComparison.Ordinal) &&
              step.EndsWith("END-ISO-10303-21;\n", StringComparison.Ordinal),
            "IFC export has a STEP header, IFC4 schema and terminator");
        check(IfcExporter.ToStep(house, stamp) == step, "IFC export is deterministic");

        // A space must use its real plan polygon, not the bounding rectangle. In
        // particular, a courtyard hole must not become occupied IFC space volume.
        var concaveDocument = BuildingDocument.Create("Concave IFC space");
        concaveDocument.Rooms.Add(new Room(Guid.NewGuid(), concaveDocument.Levels[0].Id,
            "L-01", "L-shaped room", new Point2(0, 0), new Point2(6, 6))
        {
            Boundary =
            [
                new(0, 0), new(6, 0), new(6, 2),
                new(2, 2), new(2, 6), new(0, 6)
            ]
        });
        var concaveStep = IfcExporter.ToStep(concaveDocument, stamp);
        var concavePolyline = concaveStep.Split('\n').Single(line =>
            line.Contains("=IFCPOLYLINE(", StringComparison.Ordinal));
        check(Regex.Matches(concavePolyline[(concavePolyline.IndexOf("IFCPOLYLINE(", StringComparison.Ordinal) + 12)..],
                  @"#\d+").Count == 7 &&
              concaveStep.Contains("IFCCARTESIANPOINT((2.,2.))", StringComparison.Ordinal) &&
              !concaveStep.Contains("IFCARBITRARYPROFILEDEFWITHVOIDS(", StringComparison.Ordinal),
            "IFC space extrusion follows all six vertices of a concave room instead of its bounding box");

        var courtyardDocument = BuildingDocument.Create("Courtyard IFC space");
        courtyardDocument.Rooms.Add(new Room(Guid.NewGuid(), courtyardDocument.Levels[0].Id,
            "C-01", "Room around courtyard", new Point2(0, 0), new Point2(10, 10))
        {
            Boundary = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            Holes = [[new(3, 3), new(7, 3), new(7, 7), new(3, 7)]]
        });
        var courtyardStep = IfcExporter.ToStep(courtyardDocument, stamp);
        check(Math.Abs(courtyardDocument.Rooms[0].Area - 84) < 1e-9 &&
              courtyardStep.Split('\n').Count(line => line.Contains("=IFCPOLYLINE(", StringComparison.Ordinal)) == 2 &&
              courtyardStep.Contains("IFCARBITRARYPROFILEDEFWITHVOIDS(.AREA.,$", StringComparison.Ordinal) &&
              courtyardStep.Contains("IFCCARTESIANPOINT((3.,3.))", StringComparison.Ordinal) &&
              IfcExporter.ToStep(courtyardDocument, stamp) == courtyardStep,
            "IFC4 space profile preserves a courtyard void and deterministic ring geometry");

        // A clockwise slab footprint is valid in the native model, but IFC swept
        // profiles must have a counter-clockwise outer boundary. Verify the actual
        // exported polyline coordinates, not just the presence of a profile entity.
        var clockwiseSlabDocument = BuildingDocument.Create("Clockwise IFC slab");
        var clockwiseBoundary = new List<Point2>
        {
            new(0, 0), new(0, 4), new(6, 4), new(6, 0)
        };
        clockwiseSlabDocument.Floors.Add(new FloorSlab(Guid.NewGuid(),
            clockwiseSlabDocument.Levels[0].Id, "Clockwise slab", clockwiseBoundary, 0.25));
        var clockwiseSlabStep = IfcExporter.ToStep(clockwiseSlabDocument, stamp);
        var planarPoints = Regex.Matches(clockwiseSlabStep,
                @"(?m)^#(\d+)=IFCCARTESIANPOINT\(\(([^,()]+),([^,()]+)\)\);$")
            .ToDictionary(match => int.Parse(match.Groups[1].Value),
                match => new Point2(
                    double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)));
        var slabPolyline = Regex.Match(clockwiseSlabStep,
            @"=IFCPOLYLINE\(\((#\d+(?:,#\d+)+)\)\);");
        var exportedRing = Regex.Matches(slabPolyline.Groups[1].Value, @"#(\d+)")
            .Select(match => planarPoints[int.Parse(match.Groups[1].Value)]).ToArray();
        check(exportedRing.Length == 5 &&
              exportedRing[0].DistanceTo(exportedRing[^1]) < 1e-9 &&
              Guanacaste3D.Core.Geometry.PolygonTriangulator.SignedDoubleArea(exportedRing[..^1]) > 0 &&
              clockwiseSlabDocument.Floors[0].Boundary.SequenceEqual(clockwiseBoundary),
            "IFC clockwise slab exports a counter-clockwise closed profile without modifying the BIM document");

        // A roof slab must not be mislabeled as a floor in downstream BIM tools.
        // Ceiling, water and terrain currently have no matching IfcSlab predefined type.
        var slabForType = house.Floors[0];
        var slabTypeCases = new (FloorKind Kind, string IfcType)[]
        {
            (FloorKind.Floor, ".FLOOR."),
            (FloorKind.Roof, ".ROOF."),
            (FloorKind.Ceiling, ".NOTDEFINED."),
            (FloorKind.Water, ".NOTDEFINED."),
            (FloorKind.Toposolid, ".NOTDEFINED.")
        };
        check(slabTypeCases.All(test =>
        {
            var typedHouse = house.Clone();
            typedHouse.Floors[0] = slabForType with
            {
                Kind = test.Kind, Shape = RoofShape.Flat, Pitch = 0, Overhang = 0, TopoPoints = null
            };
            var slabId = IfcExporter.ToGlobalId(slabForType.Id);
            var line = IfcExporter.ToStep(typedHouse, stamp).Split('\n')
                .Single(row => row.Contains($"=IFCSLAB('{slabId}',", StringComparison.Ordinal));
            return line.EndsWith($",{test.IfcType});", StringComparison.Ordinal);
        }), "IFC4 slab predefined types distinguish roofs, floors and unsupported categories");

        // A slab's ElevationOffset describes its TOP relative to the containing storey.
        // The IFC swept solid grows upward from its local placement at the underside.
        var sourceSlab = house.Floors[0];
        var shiftedHouse = house.Clone();
        shiftedHouse.Floors[0] = sourceSlab with { ElevationOffset = 0.4375 };
        var shiftedStep = IfcExporter.ToStep(shiftedHouse, stamp);
        var raisedBottom = IfcExporter.Real(0.4375 - sourceSlab.Thickness);
        check(shiftedStep.Contains($"IFCCARTESIANPOINT((0.,0.,{raisedBottom}))", StringComparison.Ordinal) &&
              !step.Contains($"IFCCARTESIANPOINT((0.,0.,{raisedBottom}))", StringComparison.Ordinal),
            "IFC slab placement preserves positive height offsets from the storey");

        shiftedHouse.Floors[0] = sourceSlab with { ElevationOffset = -0.625 };
        var loweredStep = IfcExporter.ToStep(shiftedHouse, stamp);
        var loweredBottom = IfcExporter.Real(-0.625 - sourceSlab.Thickness);
        check(loweredStep.Contains($"IFCCARTESIANPOINT((0.,0.,{loweredBottom}))", StringComparison.Ordinal) &&
              !step.Contains($"IFCCARTESIANPOINT((0.,0.,{loweredBottom}))", StringComparison.Ordinal),
            "IFC slab placement preserves negative height offsets from the storey");

        // IFC4 operation and partitioning enums must describe the modeled family and instance.
        // Use the widest hosted opening to keep three/four-light variants valid.
        string ExportOpening(OpeningKind kind, OpeningOperation operation, bool flipHand = false, int lights = 0)
        {
            var variant = house.Clone();
            var hosted = variant.Openings.Where(item => item.Kind == kind)
                .OrderByDescending(item => item.Width).First();
            var typeIndex = variant.OpeningTypes.FindIndex(item => item.Id == hosted.OpeningTypeId);
            variant.OpeningTypes[typeIndex] = variant.OpeningTypes[typeIndex] with
            {
                Operation = operation,
                GlazingLightCount = lights
            };
            var openingIndex = variant.Openings.FindIndex(item => item.Id == hosted.Id);
            variant.Openings[openingIndex] = hosted with { FlipHand = flipHand };
            var entityName = kind == OpeningKind.Door ? "IFCDOOR" : "IFCWINDOW";
            var globalId = IfcExporter.ToGlobalId(hosted.Id);
            return IfcExporter.ToStep(variant, stamp).Split('\n')
                .Single(row => row.Contains($"{entityName}('{globalId}',", StringComparison.Ordinal));
        }

        check(ExportOpening(OpeningKind.Door, OpeningOperation.Standard)
                  .Contains(",.SINGLE_SWING_LEFT.,$)", StringComparison.Ordinal) &&
              ExportOpening(OpeningKind.Door, OpeningOperation.Standard, flipHand: true)
                  .Contains(",.SINGLE_SWING_RIGHT.,$)", StringComparison.Ordinal),
            "IFC door swing direction follows the instance hand");

        // The standard door mesher generates two leaves above 1.2 m. Keep the IFC
        // operation aligned with the actual 3D assembly, not just the type's name.
        var doubleDoorHouse = house.Clone();
        var entryDoor = doubleDoorHouse.Openings.First(item => item.Kind == OpeningKind.Door && item.Width == 1.0);
        var entryIndex = doubleDoorHouse.Openings.FindIndex(item => item.Id == entryDoor.Id);
        var entryTypeIndex = doubleDoorHouse.OpeningTypes.FindIndex(item => item.Id == entryDoor.OpeningTypeId);
        doubleDoorHouse.Openings[entryIndex] = entryDoor with { Width = 1.5 };
        doubleDoorHouse.OpeningTypes[entryTypeIndex] = doubleDoorHouse.OpeningTypes[entryTypeIndex] with { Width = 1.5 };
        var doubleDoorId = IfcExporter.ToGlobalId(entryDoor.Id);
        var doubleDoorLine = IfcExporter.ToStep(doubleDoorHouse, stamp).Split('\n')
            .Single(row => row.Contains($"IFCDOOR('{doubleDoorId}',", StringComparison.Ordinal));
        check(doubleDoorLine.Contains(",.DOUBLE_DOOR_SINGLE_SWING.,$)", StringComparison.Ordinal),
            "IFC wide standard doors export as double-leaf swing, matching the BIM mesher");
        check(ExportOpening(OpeningKind.Door, OpeningOperation.Sliding)
                  .Contains(",.DOUBLE_DOOR_SLIDING.,$)", StringComparison.Ordinal) &&
              ExportOpening(OpeningKind.Door, OpeningOperation.Folding)
                  .Contains(",.DOUBLE_DOOR_FOLDING.,$)", StringComparison.Ordinal) &&
              ExportOpening(OpeningKind.Door, OpeningOperation.Overhead)
                  .Contains(",.ROLLINGUP.,$)", StringComparison.Ordinal) &&
              ExportOpening(OpeningKind.Door, OpeningOperation.Revolving)
                  .Contains(",.REVOLVING.,$)", StringComparison.Ordinal),
            "IFC door operations reflect sliding, folding, overhead and revolving families");
        check(ExportOpening(OpeningKind.Window, OpeningOperation.Sliding, lights: 2)
                  .Contains(",.DOUBLE_PANEL_VERTICAL.,$)", StringComparison.Ordinal) &&
              ExportOpening(OpeningKind.Window, OpeningOperation.Hung)
                  .Contains(",.DOUBLE_PANEL_HORIZONTAL.,$)", StringComparison.Ordinal) &&
              ExportOpening(OpeningKind.Window, OpeningOperation.Sliding, lights: 3)
                  .Contains(",.TRIPLE_PANEL_VERTICAL.,$)", StringComparison.Ordinal),
            "IFC window partitioning matches the modeled pane layout");
        check(ExportOpening(OpeningKind.Window, OpeningOperation.Sliding, lights: 4)
                  .Contains(",.NOTDEFINED.,$)", StringComparison.Ordinal),
            "IFC avoids falsely labeling four-pane windows as single-panel");

        var entities = new Dictionary<int, (string Type, List<string> Attributes)>();
        var data = step[(step.IndexOf("DATA;\n", StringComparison.Ordinal) + 6)..step.IndexOf("ENDSEC;\nEND-ISO", StringComparison.Ordinal)];
        var malformed = 0;
        foreach (var line in data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"^#(\d+)=([A-Z0-9]+)\((.*)\);$");
            if (!match.Success) { malformed++; continue; }
            entities[int.Parse(match.Groups[1].Value)] = (match.Groups[2].Value, SplitTopLevel(match.Groups[3].Value));
        }
        check(malformed == 0 && entities.Keys.Order().SequenceEqual(Enumerable.Range(1, entities.Count)),
            "IFC DATA lines are well-formed and sequentially numbered");
        var dangling = Regex.Matches(data, @"#(\d+)").Select(m => int.Parse(m.Groups[1].Value))
            .Count(reference => !entities.ContainsKey(reference));
        check(dangling == 0, "Every IFC entity reference resolves");
        var wrongArity = entities.Values.Where(e => !Arity.TryGetValue(e.Type, out var n) || n != e.Attributes.Count)
            .Select(e => $"{e.Type}/{e.Attributes.Count}").Distinct().ToArray();
        check(wrongArity.Length == 0, "IFC entities match IFC4 attribute counts" +
            (wrongArity.Length == 0 ? "" : ": " + string.Join(", ", wrongArity)));

        int Count(string type) => entities.Values.Count(e => e.Type == type);
        check(Count("IFCWALL") == house.Walls.Count && Count("IFCSLAB") == house.Floors.Count &&
              Count("IFCOPENINGELEMENT") == house.Openings.Count &&
              Count("IFCDOOR") + Count("IFCWINDOW") == house.Openings.Count &&
              Count("IFCRELVOIDSELEMENT") == house.Openings.Count && Count("IFCRELFILLSELEMENT") == house.Openings.Count &&
              Count("IFCSPACE") == house.Rooms.Count && Count("IFCBUILDINGSTOREY") == house.Levels.Count,
            "IFC export contains every wall, slab, opening, fill, space and storey");
        var globalIds = entities.Values.Where(e => e.Type is not ("IFCSIUNIT" or "IFCUNITASSIGNMENT") &&
                e.Attributes.Count > 0 && Regex.IsMatch(e.Attributes[0], @"^'[0-9A-Za-z_$]{22}'$"))
            .Select(e => e.Attributes[0]).ToArray();
        check(globalIds.Length > house.Walls.Count && globalIds.Distinct().Count() == globalIds.Length,
            "IFC GlobalIds are unique");
        check(step.Contains("IFCSPACE(", StringComparison.Ordinal) &&
              step.Contains(@"'Sala-comedor-cocina'", StringComparison.Ordinal),
            "IFC spaces carry room numbers and names");
    }

    /// <summary>Splits STEP attributes on commas outside nested parentheses and strings.</summary>
    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var inString = false;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'') inString = !inString; // '' escapes toggle twice and cancel out
            else if (inString) continue;
            else if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>Independent decoder used only to verify the encoder.</summary>
    private static Guid DecodeGlobalId(string globalId)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
        var value = System.Numerics.BigInteger.Zero;
        foreach (var c in globalId) value = value * 64 + alphabet.IndexOf(c);
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var padded = new byte[16];
        bytes.CopyTo(padded, 16 - bytes.Length);
        return new Guid(padded, bigEndian: true);
    }
}
