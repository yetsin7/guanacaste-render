using Guanacaste3D.Core;

/// <summary>
/// Checks the Revit family (.rfa) reader against real families when they are present on
/// this machine. The checks are skipped with a notice when the folder does not exist, so the
/// suite stays portable. They verify category detection, size parsing, and rejection of
/// families that are not openings.
/// </summary>
static class RevitFamilyImportChecks
{
    /// <summary>Folder of the Revit 2026 families used during manual testing.</summary>
    private const string FamilyFolder = @"C:\Users\Yetsin\Documents\Arquitectura\Planos de Mi Casa\Familias Revit 2026";

    /// <summary>Runs the family checks against the folder when it exists.</summary>
    public static void Run(Action<bool, string> check)
    {
        if (!Directory.Exists(FamilyFolder))
        {
            Console.WriteLine("INFO: Revit family folder not found; RFA checks skipped.");
            return;
        }

        var door = Path.Combine(FamilyFolder,
            "Doors_Sliding_Swedoor_JELD-WEN_Interior_HP_Acoustic_Flush_40_On-Wall_3810_Single_1.0.rfa");
        if (File.Exists(door))
        {
            var imported = DoorFamilyImport.Read(door, kind: OpeningKind.Door);
            check(imported.SourceFormat == "RFA" && Math.Abs(imported.Width - 0.986) < 1e-9 &&
                  Math.Abs(imported.Height - 2.089) < 1e-9,
                "Revit door family yields its type size from the PartAtom metadata");
            var asynchronous = DoorFamilyImport.ReadAsync(door, DoorFamilyImport.PlanDoorHeight, OpeningKind.Door,
                progress: null).GetAwaiter().GetResult();
            check(asynchronous == imported, "Background import returns the same dimensions as the synchronous read");
        }

        var beam = Path.Combine(FamilyFolder, "PERLIN 1.5x3.rfa");
        if (File.Exists(beam))
        {
            var rejected = false;
            try { DoorFamilyImport.Read(beam, kind: OpeningKind.Door); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "A Revit structural beam is rejected because it is not an opening");
        }
    }
}
