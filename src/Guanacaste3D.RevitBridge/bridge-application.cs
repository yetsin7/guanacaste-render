using System.Diagnostics;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Guanacaste3D.RevitBridge;

/// <summary>What Guanacaste 3D asks Revit to do: open <see cref="Input"/>, write <see cref="Output"/>, report in <see cref="Status"/>.</summary>
internal sealed record ExportJob(string Input, string Output, string Status);

/// <summary>
/// The add-in. Started by Guanacaste 3D with <c>G3D_REVIT_JOB</c> pointing at a job file, it opens the project as soon
/// as Revit is ready, writes the exchange file, closes Revit and answers every dialog on the way so nothing waits for
/// a person. Otherwise it adds a "Guanacaste 3D" ribbon tab with two buttons: export a .g3dx file, and open the model
/// in Guanacaste 3D directly.
/// </summary>
public sealed class BridgeApplication : IExternalApplication
{
    private ExportJob? _job;
    private bool _exitRequested;

    public Result OnStartup(UIControlledApplication application)
    {
        var jobPath = Environment.GetEnvironmentVariable("G3D_REVIT_JOB");
        if (!string.IsNullOrWhiteSpace(jobPath) && File.Exists(jobPath))
        {
            _job = JsonSerializer.Deserialize<ExportJob>(File.ReadAllText(jobPath));
            application.ControlledApplication.ApplicationInitialized += OnInitialized;
            application.ControlledApplication.FailuresProcessing += OnFailures;
            application.DialogBoxShowing += OnDialog;
            application.Idling += OnIdling;
            return Result.Succeeded;
        }
        const string tab = "Guanacaste 3D";
        try { application.CreateRibbonTab(tab); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { /* the tab exists already */ }
        var panel = application.CreateRibbonPanel(tab, "Intercambio");
        var assembly = typeof(BridgeApplication).Assembly.Location;
        panel.AddItem(new PushButtonData("G3DOpen", "Abrir en\nGuanacaste 3D", assembly, typeof(OpenInGuanacasteCommand).FullName)
        {
            LargeImage = Icon("open", 32),
            Image = Icon("open", 16),
            ToolTip = "Exporta el modelo y lo abre en Guanacaste 3D como un proyecto editable, listo para renderizar.",
            LongDescription = "Muros, puertas, ventanas, losas, cielos y techos planos se convierten en elementos nativos; " +
                              "muebles, equipos, escaleras y demás elementos, en familias .g3df."
        });
        panel.AddItem(new PushButtonData("G3DExport", "Exportar\n.g3dx", assembly, typeof(ExportCommand).FullName)
        {
            LargeImage = Icon("export", 32),
            Image = Icon("export", 16),
            ToolTip = "Guarda el modelo como archivo de intercambio .g3dx para abrirlo en Guanacaste 3D en cualquier computadora."
        });
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    /// <summary>A ribbon icon embedded in the add-in, such as "export" at 32 pixels.</summary>
    internal static BitmapSource? Icon(string name, int size)
    {
        using var stream = typeof(BridgeApplication).Assembly.GetManifestResourceStream($"Guanacaste3D.RevitBridge.resources.{name}-{size}.png");
        if (stream is null) return null;
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private void OnInitialized(object? sender, ApplicationInitializedEventArgs e)
    {
        if (_job is null || sender is not Application application) return;
        var clock = Stopwatch.StartNew();
        Document? document = null;
        try
        {
            Report(new { state = "opening" });
            var path = ModelPathUtils.ConvertUserVisiblePathToModelPath(_job.Input);
            var options = new OpenOptions { Audit = false };
            if (BasicFileInfo.Extract(_job.Input).IsWorkshared)
            {
                options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
            }
            document = application.OpenDocumentFile(path, options);
            Report(new { state = "exporting" });
            var exporter = new ExchangeExporter(document);
            exporter.Export(_job.Output, fraction => Report(new { state = "exporting", progress = fraction }));
            Report(new
            {
                state = "done", ok = true, elements = exporter.ElementCount, triangles = exporter.TriangleCount,
                seconds = clock.Elapsed.TotalSeconds
            });
        }
        catch (Exception ex)
        {
            Report(new { state = "done", ok = false, error = ex.GetType().Name + ": " + ex.Message });
        }
        finally
        {
            try { document?.Close(false); }
            catch (Exception) { /* Revit is closing anyway */ }
            _exitRequested = true;
        }
    }

    /// <summary>Writes the job status atomically so Guanacaste never reads half a file.</summary>
    private void Report(object status)
    {
        if (_job is null) return;
        var temporary = _job.Status + ".partial";
        File.WriteAllText(temporary, JsonSerializer.Serialize(status));
        File.Move(temporary, _job.Status, overwrite: true);
    }

    /// <summary>Upgrade notices, missing links and similar dialogs are accepted with their default answer.</summary>
    private static void OnDialog(object? sender, DialogBoxShowingEventArgs e) =>
        e.OverrideResult(e is TaskDialogShowingEventArgs ? (int)TaskDialogResult.Close : 1);

    /// <summary>Once the job is written, Revit closes itself at its next idle moment.</summary>
    private void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (!_exitRequested || sender is not UIApplication application) return;
        application.Idling -= OnIdling;
        try { application.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit)); }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { /* already exiting */ }
    }

    /// <summary>Warnings raised while opening are dismissed; they do not change the geometry being read.</summary>
    private static void OnFailures(object? sender, FailuresProcessingEventArgs e)
    {
        var accessor = e.GetFailuresAccessor();
        accessor.DeleteAllWarnings();
        e.SetProcessingResult(FailureProcessingResult.Continue);
    }

    /// <summary>Asks for the scope and exports the active project to <paramref name="path"/> with a progress window.</summary>
    internal static ExchangeExporter? ExportActive(ExternalCommandData data, string title, string action, Func<Document, string?> choosePath,
        out string? path, ref string message)
    {
        path = null;
        var document = data.Application.ActiveUIDocument?.Document;
        if (document is null || document.IsFamilyDocument)
        {
            message = "Abra un proyecto de Revit para exportarlo.";
            return null;
        }
        var (accepted, view) = ExportDialog.Ask(title, action, document, document.ActiveView as View3D is { IsTemplate: false } active ? active : null);
        if (!accepted || (path = choosePath(document)) is null) return null;
        var progress = new ExportProgressWindow(document.Title);
        progress.Show();
        try
        {
            var exporter = new ExchangeExporter(document, view);
            exporter.Export(path, fraction => progress.Report(fraction));
            return exporter;
        }
        finally
        {
            progress.Close();
        }
    }
}

/// <summary>The "Exportar .g3dx" button: exports the active project to a file the user chooses.</summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class ExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var exporter = BridgeApplication.ExportActive(commandData, "Exportar a Guanacaste 3D", "Exportar…", document =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Intercambio Guanacaste 3D (*.g3dx)|*.g3dx",
                FileName = Path.GetFileNameWithoutExtension(document.PathName is { Length: > 0 } path ? path : document.Title) + ".g3dx"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }, out var saved, ref message);
        if (exporter is null) return string.IsNullOrEmpty(message) ? Result.Cancelled : Result.Failed;
        TaskDialog.Show("Guanacaste 3D",
            $"Se exportaron {exporter.ElementCount:N0} elementos ({exporter.TriangleCount:N0} triángulos) a\n{saved}\n\n" +
            "Abra el archivo en Guanacaste 3D (Importar, o arrástrelo a la ventana) para convertirlo en un proyecto editable.");
        return Result.Succeeded;
    }
}

/// <summary>The "Abrir en Guanacaste 3D" button: exports to a temporary file and opens it in Guanacaste 3D.</summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class OpenInGuanacasteCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var application = GuanacasteLocator.Find();
        if (application is null)
        {
            TaskDialog.Show("Guanacaste 3D",
                "No se encontró Guanacaste 3D en esta computadora. Ábralo una vez (así queda registrado) o use «Exportar .g3dx» " +
                "y abra el archivo desde Guanacaste 3D.");
            return Result.Cancelled;
        }
        var exporter = BridgeApplication.ExportActive(commandData, "Abrir en Guanacaste 3D", "Exportar y abrir", document =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "Guanacaste3D", "revit");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, string.Concat(document.Title.Split(Path.GetInvalidFileNameChars())) + ".g3dx");
        }, out var saved, ref message);
        if (exporter is null) return string.IsNullOrEmpty(message) ? Result.Cancelled : Result.Failed;
        Process.Start(new ProcessStartInfo(application, $"\"{saved}\"") { UseShellExecute = false });
        return Result.Succeeded;
    }
}

/// <summary>
/// Finds Guanacaste3D.Desktop.exe: Guanacaste writes its location to %LOCALAPPDATA%\Guanacaste3D\application-path.txt
/// every time it starts, and the add-in installer may leave a copy next to the add-in.
/// </summary>
internal static class GuanacasteLocator
{
    public static string? Find()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste3D", "application-path.txt"),
            Path.Combine(Path.GetDirectoryName(typeof(GuanacasteLocator).Assembly.Location)!, "application-path.txt")
        };
        foreach (var file in candidates)
        {
            try
            {
                if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } path && File.Exists(path)) return path;
            }
            catch (IOException) { /* try the next one */ }
        }
        return null;
    }
}
