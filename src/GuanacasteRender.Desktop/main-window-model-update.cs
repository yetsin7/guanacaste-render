using System.IO;
using System.Windows;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Import;
using Guanacaste3D.Core.Interop;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Keeps a render project connected to the model it came from: notices when the modeling application saves a newer
/// model and merges it in as one undoable step that keeps cameras, photos, materials and placed objects.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan ModelWatchInterval = TimeSpan.FromSeconds(2);

    private RenderModelLink? _modelLink;
    // Common models by sync token: undoing an update returns the project to a token whose model is still here.
    private readonly Dictionary<string, BuildingDocument> _modelBaselines = new(StringComparer.Ordinal);
    private DispatcherTimer? _modelWatch;
    private bool? _modelUpdateShown;

    /// <summary>Loads or infers the model link of a freshly opened project and starts watching its model file.</summary>
    private void AttachModelLink(BuildingDocument document, string? filename)
    {
        _modelBaselines.Clear();
        _modelLink = null;
        _modelUpdateShown = null;
        if (!App.RenderOnly) return;
        if (filename is not null)
        {
            var token = RenderModelUpdate.SyncTokenOf(document);
            if (RenderModelLinkFile.Load(filename) is { } saved)
            {
                _modelLink = saved with { Baseline = null };
                if (saved.Baseline is not null) _modelBaselines[saved.SyncToken] = saved.Baseline;
            }
            else if (string.Equals(Path.GetExtension(filename), ProjectFile.NativeExtension, StringComparison.OrdinalIgnoreCase))
            {
                // A project opened straight from the modeling application is its own model source.
                _modelLink = new RenderModelLink { SourcePath = Path.GetFullPath(filename), SyncToken = token }.WithStampOf(filename);
                _modelBaselines[token] = document.Clone();
            }
        }
        if (_modelWatch is null)
        {
            _modelWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = ModelWatchInterval };
            _modelWatch.Tick += (_, _) => RefreshModelUpdateState();
            _modelWatch.Start();
        }
        RefreshModelUpdateState();
    }

    /// <summary>The model file of the open project, or null when it has none that still exists.</summary>
    private string? ResolvedModelSource() =>
        _modelLink is null ? null : RenderModelLinkFile.ResolveSource(_modelLink, _filePath);

    /// <summary>True when the model file changed since the project last exchanged data with it.</summary>
    internal bool ModelUpdateAvailable => _modelLink?.SourceChanged(ResolvedModelSource()) == true;

    /// <summary>Reflects in the studio bar whether a newer model is waiting to be brought in.</summary>
    private void RefreshModelUpdateState()
    {
        var available = ModelUpdateAvailable;
        if (_modelUpdateShown == available) return;
        _modelUpdateShown = available;
        var source = ResolvedModelSource();
        RenderPanelControl.SetModelUpdateState(available, source is null ? null : Path.GetFileName(source));
    }

    private static bool SamePath(string? first, string? second) =>
        first is not null && second is not null &&
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes the model link beside a project that was just saved at <paramref name="path"/>.</summary>
    private void StoreModelLink(string path)
    {
        if (_modelLink is null) return;
        var token = RenderModelUpdate.SyncTokenOf(_session.Document);
        try
        {
            if (SamePath(_modelLink.SourcePath, path))
            {
                // Saving onto the model file itself makes the saved state the model both applications share.
                _modelBaselines[token] = _session.Document.Clone();
                _modelLink = (_modelLink with { SyncToken = token }).WithStampOf(path);
            }
            else
            {
                RenderModelLinkFile.Save(path, _modelLink with { SyncToken = token, Baseline = _modelBaselines.GetValueOrDefault(token) });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = GetText("ModelUpdate.LinkNotSaved");
        }
        _modelUpdateShown = null;
        RefreshModelUpdateState();
    }

    /// <summary>
    /// Merges the model at <paramref name="path"/> (or the linked model) into the open project. Interactive runs
    /// ask for a file when none is linked, confirm a different project and explain conflicts; automated runs never
    /// open a dialog and return null instead.
    /// </summary>
    internal RenderModelUpdateReport? UpdateModelFromSource(string? path = null, bool interactive = true)
    {
        path ??= ResolvedModelSource();
        if (path is null)
        {
            if (!interactive) return null;
            var dialog = new OpenFileDialog { Filter = GetText("ModelUpdate.Filter"), Title = GetText("ModelUpdate.ChooseTitle"), CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return null;
            path = dialog.FileName;
        }
        path = Path.GetFullPath(path);
        var name = Path.GetFileName(path);
        RenderModelUpdateResult result;
        BuildingDocument incoming;
        try
        {
            incoming = ProjectFile.Load(path);
            if (incoming.Id != _session.Document.Id && (!interactive || MessageBox.Show(this, FormatText("ModelUpdate.DifferentProject", name),
                    GetText("ModelUpdate.Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes))
            {
                StatusText.Text = GetText("ModelUpdate.DifferentProjectSkipped");
                return null;
            }
            RenderModelUpdate.RebaseAssets(incoming, Path.GetDirectoryName(path)!);
            var token = RenderModelUpdate.SyncTokenOf(_session.Document);
            result = RenderModelUpdate.Merge(_modelBaselines.GetValueOrDefault(token), _session.Document, incoming);
            var link = (_modelLink ?? new RenderModelLink()) with { SourcePath = path, RelativeSourcePath = null };
            if (result.Report.ProjectChanged)
            {
                token = Guid.NewGuid().ToString("N");
                _session.ApplyModelUpdate(result.Document, token);
            }
            _modelBaselines[token] = incoming;
            _modelLink = (link with { SyncToken = token }).WithStampOf(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            StatusText.Text = FormatText("ModelUpdate.Failed", name);
            if (interactive)
                MessageBox.Show(this, FormatText("ModelUpdate.Failed", name) + Environment.NewLine + LocalizedErrors.Translate(exception.Message),
                    GetText("ModelUpdate.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        if (!SamePath(path, _filePath)) AdoptModelLinks(path);
        _modelUpdateShown = null;
        RefreshModelUpdateState();
        var report = result.Report;
        StatusText.Text = report.ProjectChanged
            ? FormatText("ModelUpdate.Summary", report.Added, report.Changed, report.Removed)
            : FormatText("ModelUpdate.UpToDate", name);
        // A clean update only reports in the status bar; a dialog appears when something needs the person's attention.
        var needsAttention = report.Conflicts.Count > 0 || report.MaterialsDropped > 0 || !report.UsedBaseline;
        if (interactive && report.ProjectChanged && needsAttention)
            MessageBox.Show(this, ModelUpdateDetails(report, name), GetText("ModelUpdate.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        return report;
    }

    /// <summary>Describes an applied update for the person: model changes, kept render work and conflicts.</summary>
    private static string ModelUpdateDetails(RenderModelUpdateReport report, string name)
    {
        var lines = new List<string>
        {
            FormatText("ModelUpdate.DetailSource", name),
            FormatText("ModelUpdate.Summary", report.Added, report.Changed, report.Removed),
            FormatText("ModelUpdate.DetailKept", report.KeptRenderEdits)
        };
        if (report.MaterialsDropped > 0) lines.Add(FormatText("ModelUpdate.DetailMaterials", report.MaterialsDropped));
        if (report.Conflicts.Count > 0) lines.Add(FormatText("ModelUpdate.DetailConflicts", report.Conflicts.Count));
        if (!report.UsedBaseline) lines.Add(GetText("ModelUpdate.DetailNoHistory"));
        lines.Add(GetText("ModelUpdate.DetailUndo"));
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    /// <summary>Adds linked models the modeling application attached since the last exchange.</summary>
    private void AdoptModelLinks(string modelPath)
    {
        try
        {
            var known = _links.Links.Select(link => link.Id).ToHashSet();
            var fresh = ModelLinkFile.Load(modelPath).Where(link => !known.Contains(link.Id)).ToList();
            if (fresh.Count == 0) return;
            _links.Links.AddRange(fresh);
            _linkLoad?.Cancel();
            _linkLoad = new CancellationTokenSource();
            _ = LoadLinksAsync(_linkLoad.Token);
        }
        catch (InvalidDataException)
        {
            // The model's own links file is damaged; the building itself was still updated.
        }
    }
}
