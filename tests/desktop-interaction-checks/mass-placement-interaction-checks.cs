using System.IO;
using System.Windows;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop;

internal static partial class InteractionChecks
{
    /// <summary>
    /// Mass placement: the ribbon offers it, the dialog lists the shipped families, and finishing a sketched area
    /// loads a library family and scatters it in a single undo step.
    /// </summary>
    private static void MassPlacementChecks()
    {
        var window = new MainWindow();
        try
        {
            var desktop = typeof(MainWindow).Assembly;
            var commands = Enum.GetValues<Guanacaste3D.Desktop.Controls.RibbonWorkspace>()
                .SelectMany(Guanacaste3D.Desktop.Controls.RibbonCatalog.GetGroups).SelectMany(group => group.Commands);
            Check(commands.Any(command => command.CommandId == "MassPlacement") &&
                  Application.Current.TryFindResource("RibbonCommand.MassPlacement") is string,
                "Massing & Site offers Lumion-style mass placement");

            var library = LibraryFolder();
            var session = (DocumentSession)typeof(MainWindow).GetField("_session", PrivateInstance)!.GetValue(window)!;
            var dialog = desktop.GetType("Guanacaste3D.Desktop.Controls.MassPlacementDialog")!;
            var families = (System.Collections.IList)dialog.GetMethod("Families")!.Invoke(null, [session.Document, library])!;
            var grass = families.Cast<object>().FirstOrDefault(family =>
                (family.GetType().GetProperty("LibraryPath")!.GetValue(family) as string)?.EndsWith("grass-tuft.g3df") == true);
            Check(families.Count >= 10 && grass is not null, "The mass placement dialog lists the shipped plant and car families");

            var request = Activator.CreateInstance(desktop.GetType("Guanacaste3D.Desktop.Controls.MassPlacementRequest")!,
                grass, true, new MassPlacementOptions { Spacing = 1, Seed = 3 })!;
            var undo = session.UndoNames.Count;
            var before = session.Document.StructuralElements.Count;
            IReadOnlyList<Point2> bed = [new(0, 0), new(6, 0), new(6, 4), new(0, 4)];
            typeof(MainWindow).GetMethod("FinishMassPlacement", PrivateInstance)!.Invoke(window, [request, bed]);
            var added = session.Document.StructuralElements.Count - before;
            var status = ((System.Windows.Controls.TextBlock)window.FindName("StatusText")).Text;
            Check(added > 8 && session.Document.ComponentFamilies.Count == 1 && session.UndoNames.Count == undo + 1,
                $"Finishing a sketched area loads the family and scatters it in one undo step ({added} added, " +
                $"{session.UndoNames.Count - undo} undo steps, status: {status})");
            session.Undo();
            Check(session.Document.StructuralElements.Count == before && session.Document.ComponentFamilies.Count == 0,
                "One undo removes the scattered plants and the family they loaded");
        }
        finally
        {
            typeof(MainWindow).GetField("_dirty", PrivateInstance)!.SetValue(window, false);
            window.Close();
        }
    }

    /// <summary>The shipped families beside the build, or in the source tree when the test runs from its own folder.</summary>
    private static string LibraryFolder()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "resources", "families");
        if (Directory.Exists(beside)) return beside;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var source = Path.Combine(folder.FullName, "src", "Guanacaste3D.Desktop", "resources", "families");
            if (Directory.Exists(source)) return source;
        }
        throw new DirectoryNotFoundException("The shipped families were not found.");
    }
}
