using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;
/// <summary>Fails the executable check suite when an expected invariant is not satisfied.</summary>
static void Check(bool valid, string explanation)
{
    if (!valid) throw new Exception("FAILED: " + explanation);
    Console.WriteLine("OK: " + explanation);
}

var finishedType = BuiltInWallTypes.CreateDefaults()[1] with
{
    Construction = WallConstruction.Block,
    LeftRepelloThickness = 0.015, LeftFineThickness = 0.003,
    RightRepelloThickness = 0.010, RightFineThickness = 0.005
};
var finishedAssembly = WallAssemblyPlanRules.For(finishedType, finishedType.Thickness);
Check(Math.Abs(finishedAssembly.CoreThickness - 0.167) < 1e-9,
    "Repello and fine plaster subtract from wall core");
Check(WallAssemblyPlanRules.LayerOffsets(finishedAssembly).Count == 4,
    "Four visible finish boundaries in a double-sided masonry wall");
Check(Math.Abs(WallAssemblyPlanRules.Stud16Inches * 1000 - 406.4) < 1e-6 &&
      Math.Abs(WallAssemblyPlanRules.Stud24Inches * 1000 - 609.6) < 1e-6,
    "Gypsum studs support exact 16 and 24 inch pitches");
Check(WallAssemblyPlanRules.StudPositions(3.0, WallAssemblyPlanRules.Stud16Inches)
    .Zip(WallAssemblyPlanRules.StudPositions(3.0, WallAssemblyPlanRules.Stud16Inches).Skip(1))
    .All(pair => pair.Second - pair.First <= WallAssemblyPlanRules.Stud16Inches + 1e-9),
    "Gypsum stud intervals never exceed maximum pitch");
foreach (var invalidLength in new[] { 0d, double.NaN, double.MaxValue, 1_000_001d * WallAssemblyPlanRules.Stud16Inches })
{
    var rejected = false;
    try { WallAssemblyPlanRules.StudPositions(invalidLength, WallAssemblyPlanRules.Stud16Inches); }
    catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "length") { rejected = true; }
    Check(rejected, "Gypsum stud layout rejects invalid or excessive lengths before allocation");
}
var longStudLayout = WallAssemblyPlanRules.StudPositions(12, WallAssemblyPlanRules.Stud24Inches);
Check(longStudLayout[0] == 0 && longStudLayout[^1] == 12 &&
      longStudLayout.Zip(longStudLayout.Skip(1)).All(pair => pair.Second > pair.First &&
          pair.Second - pair.First <= WallAssemblyPlanRules.Stud24Inches + 1e-9),
    "Long gypsum walls preserve exact endpoints and safe stud pitch");
var layerDocument = BuildingDocument.Create();
layerDocument.WallTypes[1] = finishedType;
layerDocument.Validate();
var reloadedLayers = layerDocument.Clone().WallTypes[1];
Check(reloadedLayers.LeftRepelloThickness == 0.015 && reloadedLayers.RightFineThickness == 0.005,
    "Wall assembly parameters survive document cloning");

var takeoffSession = new DocumentSession(layerDocument);
var takeoffWallId = takeoffSession.AddWall(layerDocument.Levels[0].Id, new Point2(0, 0), new Point2(5, 0));
takeoffSession.AddOpening(takeoffWallId, OpeningKind.Door, offset: 2);
takeoffSession.AddOpening(takeoffWallId, OpeningKind.Window, offset: 4, width: 0.8, sill: 1, height: 1);
var takeoffWall = takeoffSession.Document.Walls.Single(wall => wall.Id == takeoffWallId);
var takeoffType = takeoffSession.Document.WallTypes.Single(type => type.Id == takeoffWall.WallTypeId);
var layerRows = WallLayerQuantitySchedule.For(takeoffWall, takeoffType, takeoffSession.Document.Openings);
var netFaceArea = 5 * 3 - 0.9 * 2.1 - 0.8 * 1;
Check(layerRows.Count == 5 && layerRows.Select(row => row.Layer).SequenceEqual(
          new[] { "LeftFine", "LeftRepello", "Core", "RightRepello", "RightFine" }) &&
      layerRows.All(row => Math.Abs(row.NetFaceArea - netFaceArea) < 1e-9),
    "Wall layer takeoff preserves layer order and subtracts door/window openings");
Check(Math.Abs(layerRows.Sum(row => row.Thickness) - takeoffWall.Thickness) < 1e-9 &&
      Math.Abs(layerRows.Sum(row => row.NetVolume) - WallGeometry.NetVolume(takeoffWall, takeoffSession.Document.Openings)) < 1e-9,
    "Per-layer volumes reconcile exactly with the net wall BIM volume");
var layerCsv = WallLayerQuantitySchedule.ToCsv(takeoffSession.Document);
Check(layerCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 6 &&
      layerCsv.Contains("LeftRepello", StringComparison.Ordinal) &&
      layerCsv.Contains("RightFine", StringComparison.Ordinal),
    "The separate layer CSV exports one deterministic row per nonzero layer");
var aggregateLayerSession = new DocumentSession(takeoffSession.Document.Clone());
var additionalTakeoffWallId = aggregateLayerSession.AddWall(
    takeoffWall.LevelId, new Point2(0, 3), new Point2(5, 3));
var additionalTakeoffWall = aggregateLayerSession.Document.Walls.Single(wall => wall.Id == additionalTakeoffWallId);
Check(additionalTakeoffWall.WallTypeId == takeoffWall.WallTypeId,
    "The wall layer summary fixture uses two walls of the same assembly type");
var layerSummaryCsv = WallLayerQuantitySchedule.ToSummaryCsv(aggregateLayerSession.Document);
var layerSummaryLines = layerSummaryCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
var coreSummary = layerSummaryLines.Single(line => line.Contains(",Core,", StringComparison.Ordinal));
var coreSummaryValues = coreSummary.Split(",Core,", StringSplitOptions.None)[1].Split(',');
Check(layerSummaryLines.Length == 6 && coreSummaryValues[1] == "2" &&
      Math.Abs(double.Parse(coreSummaryValues[2], System.Globalization.CultureInfo.InvariantCulture) -
               (netFaceArea + 5 * 3)) < 1e-6 &&
      Math.Abs(double.Parse(coreSummaryValues[3], System.Globalization.CultureInfo.InvariantCulture) -
               (netFaceArea + 5 * 3) * layerRows.Single(row => row.Layer == "Core").Thickness) < 1e-6,
    "Wall layer summary combines two walls of one type without losing opening deductions");
Check(WallLayerQuantitySchedule.ToSummaryCsv(aggregateLayerSession.Document) == layerSummaryCsv,
    "Wall layer summary CSV remains deterministic across repeated exports");
var revealRows = WallOpeningRevealSchedule.For(takeoffWall, takeoffType, takeoffSession.Document.Openings);
var openingDimensions = takeoffSession.Document.Openings.ToArray();
var expectedJambArea = openingDimensions.Sum(opening => 2 * opening.Height) * takeoffWall.Thickness;
var expectedHeadArea = openingDimensions.Sum(opening => opening.Width) * takeoffWall.Thickness;
var expectedSillArea = openingDimensions.Where(opening => opening.Sill > 0)
    .Sum(opening => opening.Width) * takeoffWall.Thickness;
Check(revealRows.Count == openingDimensions.Length * layerRows.Count &&
      Math.Abs(revealRows.Sum(row => row.JambArea) - expectedJambArea) < 1e-9 &&
      Math.Abs(revealRows.Sum(row => row.HeadArea) - expectedHeadArea) < 1e-9 &&
      Math.Abs(revealRows.Sum(row => row.SillArea) - expectedSillArea) < 1e-9,
    "Rough-opening takeoff measures jambs, heads and only elevated sills by wall layer");
Check(revealRows.Where(row => row.OpeningKind == OpeningKind.Door).All(row => row.SillArea == 0) &&
      revealRows.All(row => Math.Abs(row.TotalArea - row.JambArea - row.HeadArea - row.SillArea) < 1e-9),
    "Door thresholds have no sill return and each reveal row reconciles");
var revealCsv = WallOpeningRevealSchedule.ToCsv(takeoffSession.Document);
Check(revealCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == revealRows.Count + 1 &&
      revealCsv.Contains("TotalRevealArea_m2", StringComparison.Ordinal) &&
      revealCsv == WallOpeningRevealSchedule.ToCsv(takeoffSession.Document),
    "Reveal CSV contains every opening-layer row and is reproducible");
var noOpeningReveals = takeoffSession.Document.Clone();
noOpeningReveals.Openings.Clear();
Check(WallOpeningRevealSchedule.ToCsv(noOpeningReveals)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 1,
    "Projects without hosted openings export a header-only reveal schedule");


var revealSummaryCsv = WallOpeningRevealSchedule.ToSummaryCsv(takeoffSession.Document);
var revealSummaryLines = revealSummaryCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
Check(revealSummaryLines.Length == 11 &&
      revealSummaryLines.Count(line => line.Contains(",Door,", StringComparison.Ordinal)) == 5 &&
      revealSummaryLines.Count(line => line.Contains(",Window,", StringComparison.Ordinal)) == 5 &&
      revealSummaryCsv == WallOpeningRevealSchedule.ToSummaryCsv(takeoffSession.Document),
    "Opening reveal summary groups each layer by door/window kind and is deterministic");
var revealSummaryAreas = revealSummaryLines.Skip(1)
    .Select(line => double.Parse(line.Split(',')[10], System.Globalization.CultureInfo.InvariantCulture)).Sum();
Check(Math.Abs(revealSummaryAreas - revealRows.Sum(row => row.TotalArea)) < 0.00001,
    "Grouped reveal totals reconcile with detailed jamb, head and sill quantities");
aggregateLayerSession.AddOpening(additionalTakeoffWallId, OpeningKind.Door, offset: 2);
var multiOpeningSummary = WallOpeningRevealSchedule.ToSummaryCsv(aggregateLayerSession.Document);
var multiOpeningCore = multiOpeningSummary.Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .Single(line => line.Contains(",Door,Core,", StringComparison.Ordinal));
Check(multiOpeningCore.Split(',')[6] == "2" &&
      multiOpeningSummary.Split('\n', StringSplitOptions.RemoveEmptyEntries)
          .Single(line => line.Contains(",Window,Core,", StringComparison.Ordinal)).Split(',')[6] == "1",
    "Reveal summary counts distinct openings of each kind across walls of the same type");
Check(WallOpeningRevealSchedule.ToSummaryCsv(noOpeningReveals)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 1,
    "Projects without openings export a header-only reveal summary");

var invalidLayerOpening = new WallOpening(Guid.NewGuid(), takeoffWallId, OpeningKind.Window, 2, 0.8, 1, 1);
var overlappingLayerOpeningsRejected = false;
try { WallLayerQuantitySchedule.For(takeoffWall, takeoffType, takeoffSession.Document.Openings.Append(invalidLayerOpening)); }
catch (InvalidDataException) { overlappingLayerOpeningsRejected = true; }
Check(overlappingLayerOpeningsRejected, "Layer takeoffs reject overlapping openings rather than double-subtracting volume");

var orderedWallSolids = WallGeometry.Solids(takeoffWall, takeoffSession.Document.Openings);
var reversedWallSolids = WallGeometry.Solids(takeoffWall, takeoffSession.Document.Openings.AsEnumerable().Reverse());
Check(orderedWallSolids.Count == 6 && orderedWallSolids.SequenceEqual(reversedWallSolids) &&
      Math.Abs(orderedWallSolids.Sum(part => (part.To - part.From) * (part.Top - part.Bottom)) -
               netFaceArea) < 1e-9,
    "Opening segmentation is deterministic for unordered door/window inputs and preserves net area");

// Many hosted families previously caused quadratic scans of every opening at each wall interval.
// Use a large but valid wall to verify the sweep's segment count and exact net BIM volume.
var denseWall = new Wall(Guid.NewGuid(), takeoffWall.LevelId,
    new Point2(0, 0), new Point2(2500, 0), 0.2, 3);
var denseOpenings = Enumerable.Range(0, 2500)
    .Select(index => new WallOpening(Guid.NewGuid(), denseWall.Id, OpeningKind.Window,
        index + 0.5, 0.3, 1, 1))
    .Reverse().ToArray();
WallGeometry.Validate([denseWall], denseOpenings);
var denseSolids = WallGeometry.Solids(denseWall, denseOpenings);
Check(denseSolids.Count == 7501 &&
      Math.Abs(WallGeometry.NetVolume(denseWall, denseOpenings) - 1350) < 1e-7,
    "Thousands of unordered hosted openings segment without losing solids or volume");


var document = BuildingDocument.Create();
document.Validate();
Check(document.Levels.Count == 2 && document.Levels.All(BaseLevels.IsProtected), "Default protected floor and roof levels");

var appearanceDocument = BuildingDocument.Create();
var appearanceSession = new DocumentSession(appearanceDocument);
var pbrAppearance = new RenderMaterialAssignment(SurfaceKind.Wall, "stock:")
{
    RoughnessOverride = 0.24, MetallicOverride = 0.36, Relief = 0.61
};
appearanceSession.SetRenderMaterial(pbrAppearance);
Check(appearanceSession.Document.RenderMaterials.Single().RoughnessOverride == 0.24 &&
      appearanceSession.Document.RenderMaterials.Single().MetallicOverride == 0.36,
    "PBR appearance overrides are stored in a project-scoped material transaction");
appearanceSession.Undo();
Check(appearanceSession.Document.RenderMaterials.Count == 0, "PBR appearance edit is undoable");
appearanceSession.Redo();
Check(appearanceSession.Document.RenderMaterials.Single().Relief == 0.61,
    "Undo-redo restores the original appearance assignment");
var appearancePath = Path.Combine(Path.GetTempPath(), $"guanacaste-pbr-appearance-{Guid.NewGuid():N}.g3d");
try
{
    ProjectFile.Save(appearancePath, appearanceSession.Document);
    var restoredAppearance = ProjectFile.Load(appearancePath).RenderMaterials.Single();
    Check(restoredAppearance.RoughnessOverride == 0.24 &&
          restoredAppearance.MetallicOverride == 0.36 &&
          restoredAppearance.Relief == 0.61,
        "Native project persistence round-trips roughness, metalness and relief without editing .g3dm");
}
finally
{
    if (File.Exists(appearancePath)) File.Delete(appearancePath);
    if (File.Exists(appearancePath + ".bak")) File.Delete(appearancePath + ".bak");
}
foreach (var invalidAppearance in new[] {
    pbrAppearance with { RoughnessOverride = double.NaN },
    pbrAppearance with { RoughnessOverride = 0.01 },
    pbrAppearance with { MetallicOverride = 1.5 }
})
{
    var rejected = false;
    try { appearanceSession.SetRenderMaterial(invalidAppearance); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected && appearanceSession.Document.RenderMaterials.Single() == pbrAppearance,
        "Invalid PBR overrides are rejected atomically without changing the material");
}
var session = new DocumentSession(document);
var id = session.AddWall(document.Levels[0].Id, new Point2(0, 0), new Point2(5, 0));
Check(session.Document.Walls.Single().Length == 5, "Metric wall geometry");
session.Undo();
Check(session.Document.Walls.Count == 0, "Undo");
session.Redo();
Check(session.Document.Walls.Single().Id == id, "Redo");
session.RemoveWall(id);
Check(session.Document.Walls.Count == 0, "Delete wall");

var failed = false;
try { session.AddWall(document.Levels[0].Id, new Point2(1, 1), new Point2(1, 1)); }
catch (InvalidDataException) { failed = true; }
Check(failed && session.Document.Walls.Count == 0, "Reject zero-length wall atomically");

session.AddWall(document.Levels[0].Id, new Point2(0, 0), new Point2(5, 0));
var hostedWallId = session.Document.Walls.Single().Id;
var doorId = session.AddOpening(hostedWallId, OpeningKind.Door, offset: 2.0);
var windowId = session.AddOpening(hostedWallId, OpeningKind.Window,
    offset: 4.0, width: 0.8, sill: 1.0, height: 1.0);
Check(session.Document.Openings.Count == 2, "Host wall has two apertures");
var hostedWall = session.Document.Walls.Single();
var netVolume = WallGeometry.NetVolume(hostedWall, session.Document.Openings);
var volumeExpected = (5 * 3 - 0.9 * 2.1 - 0.8 * 1.0) * 0.20;
Check(Math.Abs(netVolume - volumeExpected) < 0.00000001, "Void volume is subtracted from wall");
failed = false;
try { session.AddOpening(hostedWallId, OpeningKind.Door, offset: 2.2); }
catch (InvalidDataException) { failed = true; }
Check(failed && session.Document.Openings.Count == 2, "Overlapping openings are rejected atomically");
failed = false;
try { session.AddOpening(hostedWallId, OpeningKind.Window, offset: 4.95); }
catch (InvalidDataException) { failed = true; }
Check(failed, "Out-of-bounds apertures are rejected");
session.Undo();
Check(session.Document.Openings.Count == 1, "Undo removes second opening");
session.Redo();
Check(session.Document.Openings.Count == 2, "Redo restores hosted opening");

var filename = Path.Combine(Path.GetTempPath(), $"guanacaste-tests-{Guid.NewGuid():N}.g3");
try
{
    ProjectFile.Save(filename, session.Document);
    Check(ProjectFile.IsNativePath(filename) && File.ReadLines(filename).First() == "G3-PROJECT/1",
        "Native projects use the .g3 extension and format header");
    Check(ProjectFile.Load(filename).Walls[0].Length == 5, "Save/load versioned project");
    Check(ProjectFile.Load(filename).Openings.Count == 2, "Save/load hosted openings");
    ProjectFile.Save(filename, session.Document);
    Check(File.Exists(filename + ".bak"), "Preserve previous file backup");
    var unsupported = session.Document.Clone();
    unsupported.SchemaVersion = 99;
    File.WriteAllText(filename, "G3-PROJECT/1\n" +
        System.Text.Json.JsonSerializer.Serialize(unsupported));
    failed = false;
    try { ProjectFile.Load(filename); } catch (InvalidDataException) { failed = true; }
    Check(failed, "Reject unsupported project version");
    File.WriteAllText(filename, "G3-PROJECT/1\n{\"SchemaVersion\":1}");
    failed = false;
    try { ProjectFile.Load(filename); } catch (InvalidDataException) { failed = true; }
    Check(failed, "Reject incomplete JSON project");
}
finally
{
    if (File.Exists(filename)) File.Delete(filename);
    if (File.Exists(filename + ".bak")) File.Delete(filename + ".bak");
}
session.RemoveWall(hostedWallId);
Check(session.Document.Openings.Count == 0, "Wall deletion cascades to its openings");
session.Undo();
Check(session.Document.Openings.Count == 2, "Undo restores hosted elements");
var originalPosition = session.Document.Walls.Single(w => w.Id == hostedWallId).Start;
session.MoveWall(hostedWallId, 2, -1);
Check(session.Document.Walls.Single(w => w.Id == hostedWallId).Start ==
    new Point2(originalPosition.X + 2, originalPosition.Y - 1),
    "Moving wall updates its location");
Check(session.Document.Openings.Count(o => o.WallId == hostedWallId) == 2,
    "Moving wall preserves hosted apertures");
var copyId = session.DuplicateWall(hostedWallId);
Check(session.Document.Walls.Count == 2 && copyId != hostedWallId,
    "Duplicating wall creates an independent BIM element");
Check(session.Document.Openings.Count(o => o.WallId == copyId) == 2,
    "Duplicating wall copies hosted openings");
session.Undo();
Check(session.Document.Walls.Count == 1 && session.Document.Openings.Count == 2,
    "Undo duplication removes copied hosted elements");
var rotatedWall = session.Document.Walls.Single(w => w.Id == hostedWallId);
session.RotateWall(hostedWallId, 90);
var afterRotation = session.Document.Walls.Single(w => w.Id == hostedWallId);
Check(Math.Abs(afterRotation.Length - rotatedWall.Length) < 0.000001,
    "Wall rotation preserves length");
Check(session.Document.Openings.Count(o => o.WallId == hostedWallId) == 2,
    "Wall rotation retains hosted openings");
session.Undo();
Check(session.Document.Walls.Single(w => w.Id == hostedWallId).End == rotatedWall.End,
    "Undo restores pre-rotation coordinates");
var firstOpening = session.Document.Openings.Single(o => o.Id == doorId);
session.UpdateOpening(doorId, firstOpening.Offset, 1.0, 0, 2.1);
Check(Math.Abs(session.Document.Openings.Single(o => o.Id == doorId).Width - 1) < 0.000001,
    "Parametric opening dimensions can be edited");
failed = false;
try { session.UpdateOpening(doorId, 4.0, 1.0, 0, 2.1); }
catch (InvalidDataException) { failed = true; }
Check(failed && session.Document.Openings.Single(o => o.Id == doorId).Offset == firstOpening.Offset,
    "Overlapping opening edit is rejected atomically");
var beforePreciseMove = session.Document.Walls.Single(w => w.Id == hostedWallId);
session.MoveWall(hostedWallId, -0.25, 0.75);
var movedPrecisely = session.Document.Walls.Single(w => w.Id == hostedWallId);
Check(Math.Abs(movedPrecisely.Start.X - beforePreciseMove.Start.X + 0.25) < 0.000001 &&
      Math.Abs(movedPrecisely.Start.Y - beforePreciseMove.Start.Y - 0.75) < 0.000001,
      "Fractional metric translation");
session.RotateWall(hostedWallId, 37.5);
Check(Math.Abs(session.Document.Walls.Single(w => w.Id == hostedWallId).Length -
    movedPrecisely.Length) < 0.000001, "Arbitrary angle preserves wall length");
session.Undo();
Check(session.Document.Walls.Single(w => w.Id == hostedWallId).End == movedPrecisely.End,
    "Undo restores precise transformation");
var schedule = QuantitySchedule.ToCsv(session.Document);
Check(schedule.Contains("NetVolume_m3") && schedule.Contains("DoorCount") &&
    schedule.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 2,
    "Quantity takeoff exports one row per wall");
Check(schedule.Contains(",1,1"), "Quantity takeoff includes door and window counts");
var formulaWallDocument = session.Document.Clone();
var formulaLevelIndex = formulaWallDocument.Levels.FindIndex(item => item.Id == hostedWall.LevelId);
formulaWallDocument.Levels[formulaLevelIndex] = formulaWallDocument.Levels[formulaLevelIndex] with
{
    Name = " \t=1+1,\"Q\""
};
var safeWallCsv = QuantitySchedule.ToCsv(formulaWallDocument);
Check(safeWallCsv.Contains("\"' \t=1+1,\"\"Q\"\"\",", StringComparison.Ordinal),
    "Wall quantity CSV escapes quotes and neutralizes formulas in level names");
var wallBeforeInvalidMove = session.Document.Walls.Single(w => w.Id == hostedWallId);
failed = false;
try { session.MoveWall(hostedWallId, double.NaN, 0); }
catch (InvalidDataException) { failed = true; }
Check(failed && session.Document.Walls.Single(w => w.Id == hostedWallId) == wallBeforeInvalidMove,
    "Non-finite wall translation is rejected atomically");
failed = false;
try { session.RotateWall(hostedWallId, double.PositiveInfinity); }
catch (InvalidDataException) { failed = true; }
Check(failed, "Non-finite wall rotation is rejected");
for (int i = 0; i < 100; i++) session.Rename("Project " + i);
for (int i = 0; i < 40; i++) session.Undo();
Check(session.Document.Name == "Project 59" && !session.CanUndo, "Undo is bounded to 40 snapshots");

var coordinatedDocument = BuildingDocument.Create();
var coordinatedSession = new DocumentSession(coordinatedDocument);
Check(coordinatedSession.Document.WallTypes.Count == 3, "New projects include built-in wall types");
var exteriorWallId = coordinatedSession.AddWall(
    coordinatedDocument.Levels[0].Id,
    new Point2(0, 0),
    new Point2(8, 0),
    wallTypeId: BuiltInWallTypes.Exterior300Id);
var exteriorWall = coordinatedSession.Document.Walls.Single(wall => wall.Id == exteriorWallId);
Check(exteriorWall.WallTypeId == BuiltInWallTypes.Exterior300Id &&
      Math.Abs(exteriorWall.Thickness - 0.30) < 0.000001,
      "Wall instances use reusable type parameters");
coordinatedSession.UpdateWallType(
    BuiltInWallTypes.Exterior300Id,
    "High-performance exterior 350 mm",
    WallFunction.Exterior,
    0.35,
    "Insulated concrete",
    "#345678",
    WallFillPattern.Crosshatch,
    "#876543",
    WallFillPattern.Diagonal);
Check(Math.Abs(coordinatedSession.Document.Walls.Single().Thickness - 0.35) < 0.000001,
    "Wall type edits propagate to instances");
var editedWallType = coordinatedSession.Document.WallTypes.Single(type => type.Id == BuiltInWallTypes.Exterior300Id);
Check(editedWallType.PlanFillColor == "#345678" &&
      editedWallType.PlanFillPattern == WallFillPattern.Crosshatch &&
      editedWallType.SectionFillColor == "#876543" &&
      editedWallType.SectionFillPattern == WallFillPattern.Diagonal,
    "Wall types coordinate independent plan and section drafting appearances");
var roomId = coordinatedSession.AddRoom(
    coordinatedDocument.Levels[0].Id,
    new Point2(0, 0),
    new Point2(4, 5),
    "Meeting Room");
Check(Math.Abs(coordinatedSession.Document.Rooms.Single(room => room.Id == roomId).Area - 20) < 0.000001,
    "Room area is coordinated from its boundary");
var floorId = coordinatedSession.AddFloor(
    coordinatedDocument.Levels[0].Id,
    [new Point2(0, 0), new Point2(8, 0), new Point2(8, 6), new Point2(0, 6)]);
Check(Math.Abs(coordinatedSession.Document.Floors.Single(floor => floor.Id == floorId).CalculateArea() - 48) < 0.000001,
    "Floor area is coordinated from its polygon");
var sheetId = coordinatedSession.AddSheet();
Check(coordinatedSession.Document.Sheets.Single(sheet => sheet.Id == sheetId).Number == "A001",
    "Drawing sheets receive stable project numbers");
var projectRows = ProjectScheduleService.Build(coordinatedSession.Document);
Check(projectRows.Count(row => row.Category is "Wall" or "Room" or "Floor" or "Sheet") == 4,
    "Project schedule coordinates modeled and documented elements");

var coordinatedFilename = Path.Combine(
    Path.GetTempPath(), $"guanacaste-coordinated-{Guid.NewGuid():N}.g3");
var legacyFilename = Path.Combine(
    Path.GetTempPath(), $"guanacaste-legacy-{Guid.NewGuid():N}.g3d.json");
try
{
    ProjectFile.Save(coordinatedFilename, coordinatedSession.Document);
    var coordinatedReload = ProjectFile.Load(coordinatedFilename);
    var reloadedWallType = coordinatedReload.WallTypes.Single(type => type.Id == BuiltInWallTypes.Exterior300Id);
    Check(coordinatedReload.Rooms.Count == 1 && coordinatedReload.Floors.Count == 1 &&
          coordinatedReload.Sheets.Count == 1 && coordinatedReload.WallTypes.Count == 3 &&
          reloadedWallType.PlanFillColor == "#345678" &&
          reloadedWallType.SectionFillPattern == WallFillPattern.Diagonal,
        "Coordinated BIM elements survive a project round trip");

    var legacyDocument = LegacyDocumentFixtures.CreateSchemaOne(coordinatedSession.Document);
    File.WriteAllText(coordinatedFilename, "G3-PROJECT/1\n" +
        System.Text.Json.JsonSerializer.Serialize(legacyDocument));
    var upgradedDocument = ProjectFile.Load(coordinatedFilename);
    Check(upgradedDocument.SchemaVersion == BuildingDocument.CurrentSchemaVersion && upgradedDocument.WallTypes.Count == 3 &&
          upgradedDocument.Views.Count >= 5 &&
          upgradedDocument.Walls.All(wall => upgradedDocument.WallTypes.Any(type => type.Id == wall.WallTypeId)),
        "Schema-one projects migrate to coordinated wall types and views");

    File.WriteAllText(legacyFilename, System.Text.Json.JsonSerializer.Serialize(legacyDocument));
    Check(ProjectFile.Load(legacyFilename).SchemaVersion == BuildingDocument.CurrentSchemaVersion,
        "Legacy .g3d.json projects remain importable");
}
finally
{
    if (File.Exists(coordinatedFilename)) File.Delete(coordinatedFilename);
    if (File.Exists(coordinatedFilename + ".bak")) File.Delete(coordinatedFilename + ".bak");
    if (File.Exists(legacyFilename)) File.Delete(legacyFilename);
}

var viewDocument = BuildingDocument.Create("View Coordination");
Check(viewDocument.Views.Any(view => view.Kind == ProjectViewKind.Elevation) &&
      viewDocument.Views.Any(view => view.Kind == ProjectViewKind.Section) &&
      viewDocument.Views.Any(view => view.Kind == ProjectViewKind.Model3D),
    "New projects include coordinated orthographic and 3D views");
var viewSession = new DocumentSession(viewDocument);
var addedLevelId = viewSession.AddLevel();
Check(viewSession.Document.Views.Any(view =>
        view.Kind == ProjectViewKind.Plan && view.LevelId == addedLevelId),
    "Adding a level creates its coordinated plan view");

var dxfSession = new DocumentSession(BuildingDocument.Create("DXF Export"));
var dxfLevelId = dxfSession.Document.Levels[0].Id;
var dxfWallId = dxfSession.AddWall(dxfLevelId, new Point2(0, 0), new Point2(6, 0));
dxfSession.AddOpening(dxfWallId, OpeningKind.Door, offset: 2);
dxfSession.AddFloor(dxfLevelId, [new(0, 0), new(6, 0), new(6, 4), new(0, 4)]);
dxfSession.AddRoom(dxfLevelId, new Point2(0, 0), new Point2(6, 4), "Sala\nComedor");
var dxf = DxfExporter.ToAscii(dxfSession.Document);
var dxfLines = dxf.Split("\r\n");
Check(dxf.StartsWith("0\r\nSECTION\r\n2\r\nHEADER", StringComparison.Ordinal) &&
      dxf.EndsWith("0\r\nEOF\r\n", StringComparison.Ordinal) && dxfLines.Contains("AC1009"),
    "DXF export is a framed R12 document");
Check(dxfLines.Length % 2 == 1 &&
      dxfLines.Where((_, index) => index % 2 == 0 && index < dxfLines.Length - 1)
          .All(code => int.TryParse(code, out _)),
    "DXF export alternates integer group codes and values");
var lineCount = dxfLines.Count(line => line == "LINE");
// Door splits the wall into two 4-edge outlines, plus 1 door chord, 4 slab edges and 4 room edges.
Check(lineCount == 8 + 1 + 4 + 4, "DXF export writes split walls, opening chord, slab and room edges");
Check(dxf.Contains(DxfExporter.LayerName(0, "DOOR"), StringComparison.Ordinal) &&
      dxf.Contains("001 Sala Comedor", StringComparison.Ordinal),
    "DXF export layers openings and sanitizes room labels");
Check(!dxf.Contains(','), "DXF numbers are culture invariant");
Check(DxfExporter.ToAscii(dxfSession.Document) == dxf, "DXF export is deterministic");

// Concave L-shaped slab: a fan triangulation would cover the notch; ear clipping must not.
Point2[] lShape = [new(0, 0), new(6, 0), new(6, 2), new(2, 2), new(2, 5), new(0, 5)];
var lTriangles = PolygonTriangulator.Triangulate(lShape);
var lArea = lTriangles.Sum(t => Math.Abs(PolygonTriangulator.SignedDoubleArea([lShape[t.A], lShape[t.B], lShape[t.C]])) / 2);
Check(lTriangles.Count == 4 && Math.Abs(lArea - 18) < 1e-9, "Concave slabs triangulate to their exact area");
var clockwiseL = lShape.Reverse().ToArray();
Check(Math.Abs(PolygonTriangulator.Triangulate(clockwiseL).Count - 4) == 0, "Clockwise outlines triangulate");
failed = false;
try { PolygonTriangulator.Triangulate([new(0, 0), new(1, 1), new(2, 2)]); }
catch (ArgumentException) { failed = true; }
Check(failed, "Degenerate outlines are rejected by the triangulator");

// A local floor outline can be placed far from the project origin without losing its area.
var distantL = lShape.Select(point => new Point2(point.X + 1_000_000_000, point.Y + 1_000_000_000)).ToArray();
var distantTriangles = PolygonTriangulator.Triangulate(distantL);
var distantArea = distantTriangles.Sum(t =>
    Math.Abs(PolygonTriangulator.SignedDoubleArea([distantL[t.A], distantL[t.B], distantL[t.C]])) / 2);
Check(distantTriangles.Count == 4 &&
      Math.Abs(PolygonTriangulator.SignedDoubleArea(distantL) / 2 - 18) < 1e-9 &&
      Math.Abs(distantArea - 18) < 1e-9,
    "Large project coordinates retain precise floor areas and triangulation");
foreach (var invalidOutline in new Point2[][]
{
    [new(0, 0), new(double.NaN, 1), new(0, 2)],
    [new(0, 0), new(double.PositiveInfinity, 1), new(0, 2)],
    [new(0, 0), new(double.MaxValue, 0), new(0, double.MaxValue)]
})
{
    var rejected = false;
    try { PolygonTriangulator.Triangulate(invalidOutline); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "Triangulation rejects non-finite coordinates or overflowed areas");
}

var renderSession = new DocumentSession(BuildingDocument.Create("Render"));
var renderLevel = renderSession.Document.Levels[0].Id;
var renderWall = renderSession.AddWall(renderLevel, new Point2(0, 0), new Point2(8, 0));
renderSession.AddOpening(renderWall, OpeningKind.Window, offset: 4, width: 1.2, sill: 0.9, height: 1.2);
renderSession.AddFloor(renderLevel, lShape);
var sceneMesh = BuildingMesher.Build(renderSession.Document, includeGround: true);
Check(sceneMesh.TriangleKinds.Contains(SurfaceKind.Glass) &&
      sceneMesh.TriangleKinds.Contains(SurfaceKind.Ground) &&
      sceneMesh.Indices.Count == sceneMesh.TriangleCount * 3,
    "Building mesher emits walls, glazing, slabs and ground");

var slabSession = new DocumentSession(BuildingDocument.Create("Slab"));
slabSession.AddFloor(slabSession.Document.Levels[0].Id, lShape, 0.2);
var floorOnly = BuildingMesher.Build(slabSession.Document);
var meshVolume = 0d;
for (var triangle = 0; triangle < floorOnly.TriangleCount; triangle++)
{
    var a = floorOnly.Positions[floorOnly.Indices[triangle * 3]];
    var b = floorOnly.Positions[floorOnly.Indices[triangle * 3 + 1]];
    var c = floorOnly.Positions[floorOnly.Indices[triangle * 3 + 2]];
    meshVolume += System.Numerics.Vector3.Dot(a, System.Numerics.Vector3.Cross(b, c)) / 6;
}
Check(Math.Abs(meshVolume - 18 * 0.2) < 1e-4, "Slab mesh is closed and outward-facing (divergence volume)");

var bvh = new Bvh(sceneMesh);
var bruteMisses = 0;
var rayRandom = new Random(7);
for (var sample = 0; sample < 2000; sample++)
{
    var origin = new System.Numerics.Vector3(rayRandom.NextSingle() * 20 - 6, rayRandom.NextSingle() * 20 - 10, rayRandom.NextSingle() * 6);
    var direction = System.Numerics.Vector3.Normalize(new(rayRandom.NextSingle() - 0.5f, rayRandom.NextSingle() - 0.5f, rayRandom.NextSingle() - 0.5f));
    var hit = new RayHit { Distance = float.MaxValue };
    bvh.Intersect(origin, direction, 1e-4f, ref hit);
    var best = float.MaxValue;
    for (var triangle = 0; triangle < sceneMesh.TriangleCount; triangle++)
    {
        var a = sceneMesh.Positions[sceneMesh.Indices[triangle * 3]];
        var e1 = sceneMesh.Positions[sceneMesh.Indices[triangle * 3 + 1]] - a;
        var e2 = sceneMesh.Positions[sceneMesh.Indices[triangle * 3 + 2]] - a;
        var p = System.Numerics.Vector3.Cross(direction, e2);
        var det = System.Numerics.Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) continue;
        var tv = origin - a;
        var u = System.Numerics.Vector3.Dot(tv, p) / det;
        var q = System.Numerics.Vector3.Cross(tv, e1);
        var v = System.Numerics.Vector3.Dot(direction, q) / det;
        var t = System.Numerics.Vector3.Dot(e2, q) / det;
        if (u >= 0 && v >= 0 && u + v <= 1 && t > 1e-4f && t < best) best = t;
    }
    if (MathF.Abs(best - hit.Distance) > 1e-3f && !(best == float.MaxValue && hit.Distance == float.MaxValue)) bruteMisses++;
}
Check(bruteMisses == 0, "BVH closest hits match brute-force ray casting on 2000 rays");

var (sceneMin, sceneMax) = BuildingMesher.Build(renderSession.Document).Bounds();
var tracer = new PathTracer(sceneMesh, 64, 40, RenderCamera.FrameBounds(sceneMin, sceneMax));
for (var pass = 0; pass < 4; pass++) tracer.RenderPass();
var image = tracer.ToBgra32();
var distinctColors = Enumerable.Range(0, image.Length / 4).Select(i => (image[i * 4], image[i * 4 + 1], image[i * 4 + 2])).Distinct().Count();
Check(tracer.SampleCount == 4 && image.Length == 64 * 40 * 4 && distinctColors > 50 &&
      Enumerable.Range(0, 64 * 40).All(i => image[i * 4 + 3] == 255),
    "Path tracer accumulates progressive samples into an opaque image");
var skyTracer = new PathTracer(sceneMesh, 8, 8,
    new RenderCamera(new(4, -20, 2), new(4, -20, 50), 30));
skyTracer.RenderPass();
var skyPixel = skyTracer.LinearPixel(4, 4);
Check(skyPixel.Z > skyPixel.X, "Path tracer sky is blue-dominant above the model");

var house = SampleProjects.CreateDemonstrationHouse();
house.Validate();
Check(house.Walls.Count == 6 && house.Openings.Count == 11 && house.Rooms.Count == 4 && house.Floors.Count == 1,
    "Demonstration house is complete and valid");
Check(Math.Abs(house.Floors[0].CalculateArea() - (12.3 * 8.3 + 6.15 * 2.35)) < 1e-6,
    "Demonstration terrace slab area is exact");
var sampleFile = Path.Combine(Path.GetTempPath(), $"guanacaste-sample-{Guid.NewGuid():N}.g3");
try
{
    ProjectFile.Save(sampleFile, house);
    var reopened = ProjectFile.Load(sampleFile);
    Check(reopened.Openings.Count == house.Openings.Count &&
          DxfExporter.ToAscii(reopened) == DxfExporter.ToAscii(house),
        "Demonstration house survives save, reopen and identical DXF export");
}
finally
{
    if (File.Exists(sampleFile)) File.Delete(sampleFile);
}
var houseScene = BuildingMesher.Build(house, includeGround: true);
var renderTimer = System.Diagnostics.Stopwatch.StartNew();
var houseTracer = new PathTracer(houseScene, 320, 200, RenderCamera.FrameBounds(BuildingMesher.Build(house).Bounds().Min, BuildingMesher.Build(house).Bounds().Max));
houseTracer.RenderPass();
renderTimer.Stop();
Console.WriteLine($"INFO: {houseScene.TriangleCount} triangles, 320x200 sample pass in {renderTimer.ElapsedMilliseconds} ms");
Check(renderTimer.ElapsedMilliseconds < 10000, "Path tracer renders a 64k-pixel pass of the sample house in under 10 s");

// Extreme but finite scene counts must never wrap memory estimates negative
// and incorrectly authorize a render that would exhaust the machine.
Check(RenderSafety.EstimateBytes(64, 64, 0, 0) ==
      RenderSettings.EstimateImageBytes(64, 64) + 64L * 64 * 16,
    "Render memory estimate preserves ordinary image buffer accounting");
Check(RenderSafety.EstimateBytes(8192, 8192, long.MaxValue, 0) == long.MaxValue,
    "Render memory estimate saturates for enormous imported scenes");
var extremeRenderPlan = QualityPlanner.Plan(RenderQuality.High,
    new HardwareProfile { LogicalCores = 8, AvailableMemoryBytes = 16L << 30, TotalMemoryBytes = 16L << 30 },
    1920, 1080, sceneTriangles: long.MaxValue);
Check(extremeRenderPlan.EstimatedBytes == long.MaxValue &&
      extremeRenderPlan.Notes.Contains(PlanNote.SceneNearBudget),
    "Quality planner never reports a huge overflowing scene as fitting memory");

GridAxisAssociationChecks.Run(Check);
RoomSeparatorBoundaryChecks.Run(Check);
AutomaticRoomSynchronizationChecks.Run(Check);
IrregularRoomChecks.Run(Check);
AdvancedRoomBoundaryChecks.Run(Check);
AgentChecks.Run(Check);
StairAndWallChecks.Run(Check);
ModernHouseChecks.Run(Check);
ModernHouseToolChecks.Run(Check);
BreezeBlockHouseChecks.Run(Check);
MeshFamilyChecks.Run(Check);
MassPlacementChecks.Run(Check);
RenderLookChecks.Run(Check);
RenderHighlightChecks.Run(Check);
RenderLayerChecks.Run(Check);
RenderSunChecks.Run(Check);
RenderMovieStorageChecks.Run(Check);
RenderDecalChecks.Run(Check);
RenderCameraEffectChecks.Run(Check);
RenderWaterChecks.Run(Check);
RenderPeopleChecks.Run(Check);
RenderWetChecks.Run(Check);
RenderGrassChecks.Run(Check);
RenderFireChecks.Run(Check);
RenderInterpolationChecks.Run(Check);
await RenderMotionChecks.Run(Check);
RenderWeatherChecks.Run(Check);
ImportedUvChecks.Run(Check);
FinishTransactionChecks.Run(Check);
RoofTakeoffChecks.Run(Check);
RevitExchangeChecks.Run(Check);
AnnotationChecks.Run(Check);
CalloutChecks.Run(Check);
FeatureEdgeSeamChecks.Run(Check);
ParameterChecks.Run(Check);
OpeningTypeChecks.Run(Check);
OpeningHostingChecks.Run(Check);
WallDimensionChecks.Run(Check);
DoorImportChecks.Run(Check);
NavigationChecks.Run(Check);
ViewVisibilityChecks.Run(Check);
RenderFxChecks.Run(Check);
MaterialTintChecks.Run(Check);
AppVersionChecks.Run(Check);
RevitFamilyImportChecks.Run(Check);
NavigationChecks.Run(Check);
OpeningFamilyChecks.Run(Check);
FormulaChecks.Run(Check);
LevelChecks.Run(Check);
GridAxisChecks.Run(Check);
LevelCopyChecks.Run(Check);
LifeSafetyChecks.Run(Check);
FloorPlanHatchChecks.Run(Check);
AreaScheduleChecks.Run(Check);
ScheduleChecks.Run(Check);
RenderPhotoChecks.Run(Check);
LocalizationChecks.Run(Check);
IfcChecks.Run(Check);
MeshExportChecks.Run(Check);
DenoiserChecks.Run(Check);
SolarChecks.Run(Check);
WallPlacementChecks.Run(Check);
PlantingChecks.Run(Check);
SiteRoofStairChecks.Run(Check);
StairRailingChecks.Run(Check);
TwoStoreyHouseChecks.Run(Check);
ToiletFamilyChecks.Run(Check);
RenderDetailChecks.Run(Check);
RenderSceneBookmarkChecks.Run(Check);
ElectricalChecks.Run(Check);
CatalogChecks.Run(Check);
PbrMaterialAuditChecks.Run(Check);
PlumbingChecks.Run(Check);
CoolingChecks.Run(Check);
SiteDrainageChecks.Run(Check);
WaterStorageChecks.Run(Check);
PhotovoltaicChecks.Run(Check);
LightingChecks.Run(Check);
NativeDocumentChecks.Run(Check);
ModelViewIntegrationChecks.Run(Check);
AiRenderChecks.Run();
Console.WriteLine("Guanacaste 3D: all core checks passed.");
