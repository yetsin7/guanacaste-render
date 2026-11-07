using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Guanacaste3D.Core;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

public partial class MainWindow : Window
{
    private DocumentSession _session;
    private Guid _activeLevelId;
    private Guid? _selectedWallId;
    private Guid? _selectedOpeningId;
    private double? _selectedWallOffset;
    private Point2? _pendingWallStart;
    private string? _filePath;
    private bool _dirty;
    private bool _drawingWall;
    private double _scale = 36;
    private Point _panOffset;
    private bool _refreshingLevels;

    /// <summary>
    /// Initializes a coordinated BIM document and its desktop workspace. With <paramref name="showStartScreen"/> and no
    /// project to open, it starts on the Home screen, where the person chooses what to work on.
    /// </summary>
    public MainWindow(string? projectPath = null, bool showStartScreen = false)
    {
        InitializeComponent();
        ConfigureApplicationFileMenu();
        InitializeGridAxisInteraction();
        ApplyDesktopOptions(DesktopOptions.Load());
        InitializeCommandCustomization();
        _session = new DocumentSession(ArchitecturalLibrary.CreateProject(GetText("Project.Untitled")));
        _activeLevelId = _session.Document.Levels[0].Id;
        _session.Changed += OnDocumentChanged;
        _session.RenderPhotosChanged += OnRenderPhotosChanged;
        InitializeRecovery();
        InitializeNavigationAndRender();
        RefreshAll();
        if (!string.IsNullOrWhiteSpace(projectPath)) OpenAssociatedDocumentPath(projectPath);
        else if (showStartScreen) ShowStartScreen();
    }

    /// <summary>Loads a project supplied by file association while preserving a usable empty document on failure.</summary>
    private void OpenProjectPath(string projectPath)
    {
        try
        {
            var document = ProjectFile.Load(projectPath);
            AttachDocument(document, ProjectFile.IsNativePath(projectPath) ? projectPath : null);
            StatusText.Text = GetText(ProjectFile.IsNativePath(projectPath)
                ? "Status.ProjectOpened"
                : "Status.LegacyProjectImported");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.OpenFailed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Marks the current project dirty and refreshes every synchronized view.</summary>
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        _dirty = true;
        _autosave?.Touch();
        // Picking must never use geometry from the previous document snapshot while
        // the asynchronous WPF scene is still being rebuilt.
        _picker = null;
        RefreshAll();
    }

    /// <summary>Photos are saved with the project, but editing one must not rebuild any view of the model.</summary>
    private void OnRenderPhotosChanged(object? sender, EventArgs e)
    {
        if (_dirty) return;
        _dirty = true;
        _autosave?.Touch();
        UpdateWindowTitle();
    }

    /// <summary>Starts a new project after protecting unsaved edits.</summary>
    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        AttachDocument(ArchitecturalLibrary.CreateProject(GetText("Project.Untitled")), null);
        StatusText.Text = GetText("Status.ProjectNew");
    }

    /// <summary>Opens the bundled demonstration house as a new, unsaved project.</summary>
    private void OpenSample_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        AttachDocument(SampleProjects.CreateDemonstrationHouse(), null);
        StatusText.Text = GetText("Status.SampleOpened");
    }

    /// <summary>Loads and validates a Guanacaste 3D project chosen by the user.</summary>
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog
        {
            Filter = GetText("Dialog.OpenProjectFilter"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var document = ProjectFile.Load(dialog.FileName);
            var nativePath = ProjectFile.IsNativePath(dialog.FileName);
            AttachDocument(document, nativePath ? dialog.FileName : null);
            StatusText.Text = GetText(nativePath ? "Status.ProjectOpened" : "Status.LegacyProjectImported");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.OpenFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Exports the coordinated wall quantity schedule as UTF-8 CSV.</summary>
    private void ExportQuantities_Click(object sender, RoutedEventArgs e) =>
        ExportText("Dialog.QuantitiesFilter", "Dialog.QuantitiesFileName", ".csv",
            () => QuantitySchedule.ToCsv(_session.Document), new System.Text.UTF8Encoding(true),
            "Status.QuantitiesExported");

    /// <summary>Exports the coordinated floor plans as metric DXF R12 drawing geometry.</summary>
    private void ExportDxf_Click(object sender, RoutedEventArgs e) =>
        ExportText("Dialog.DxfFilter", "Dialog.DxfFileName", ".dxf",
            () => DxfExporter.ToAscii(_session.Document), System.Text.Encoding.ASCII,
            "Status.DxfExported");

    /// <summary>Exports the BIM model as an IFC4 STEP file for exchange with other BIM tools.</summary>
    private void ExportIfc_Click(object sender, RoutedEventArgs e) =>
        ExportText("Dialog.IfcFilter", "Dialog.IfcFileName", ".ifc",
            () => Guanacaste3D.Core.Interop.IfcExporter.ToStep(_session.Document, DateTimeOffset.Now),
            new System.Text.UTF8Encoding(false), "Status.IfcExported");

    /// <summary>Exports the 3D model as glTF binary or Wavefront OBJ (with an MTL beside it).</summary>
    private void ExportModel3D_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = GetText("Dialog.Model3DFilter"),
            FileName = _session.Document.Name + ".glb",
            AddExtension = true,
            DefaultExt = ".glb"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var mesh = Guanacaste3D.Core.Geometry.BuildingMesher.Build(_session.Document);
            if (string.Equals(System.IO.Path.GetExtension(dialog.FileName), ".obj", StringComparison.OrdinalIgnoreCase))
            {
                var mtlPath = System.IO.Path.ChangeExtension(dialog.FileName, ".mtl");
                var (obj, mtl) = Guanacaste3D.Core.Interop.MeshExporter.ToObj(mesh, System.IO.Path.GetFileName(mtlPath));
                System.IO.File.WriteAllText(dialog.FileName, obj, new System.Text.UTF8Encoding(false));
                System.IO.File.WriteAllText(mtlPath, mtl, new System.Text.UTF8Encoding(false));
            }
            else
            {
                System.IO.File.WriteAllBytes(dialog.FileName,
                    Guanacaste3D.Core.Interop.MeshExporter.ToGlb(mesh, _session.Document.Name));
            }
            StatusText.Text = GetText("Status.Model3DExported");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.ExportFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Asks for a destination and writes a generated text export with error reporting.</summary>
    private void ExportText(string filterKey, string fileNameKey, string extension,
        Func<string> generate, System.Text.Encoding encoding, string statusKey)
    {
        var dialog = new SaveFileDialog
        {
            Filter = GetText(filterKey),
            FileName = GetText(fileNameKey),
            AddExtension = true,
            DefaultExt = extension
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName, generate(), encoding);
            StatusText.Text = GetText(statusKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.ExportFailed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Saves the project to its current location or requests one when needed.</summary>
    private void Save_Click(object sender, RoutedEventArgs e) => SaveProject(false);

    /// <summary>Saves a copy of the project at a user-selected location.</summary>
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveProject(true);

    /// <summary>Persists the active document and returns whether the operation completed.</summary>
    private bool SaveProject(bool choosePath)
    {
        RenderPanelControl.SaveMovieNow();
        RenderPanelControl.SaveOceanNow();
        var path = _filePath;
        if (choosePath || path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = GetText("Dialog.ProjectFilter"),
                FileName = _session.Document.Name + ProjectFile.NativeExtension,
                AddExtension = true,
                DefaultExt = ProjectFile.NativeExtension
            };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        try
        {
            // The render view travels with the project, so reopening it shows the same camera, sun and filters.
            try { _session.StoreRenderSetup(RenderPanelControl.CaptureSetupForProject(_navigation, path!)); }
            catch (InvalidDataException) { }
            ProjectFile.Save(path!, _session.Document);
            _links.Save(path!);
            _filePath = path;
            RenderPanelControl.SetProjectFilePath(_filePath);
            _dirty = false;
            // The project is safe in its own file now, so its automatic copy has done its job.
            DiscardRecoveryCopy();
            RememberRecent(path!, _session.Document);
            UpdateWindowTitle();
            StatusText.Text = FormatText("Status.ProjectSaved", System.IO.Path.GetFileName(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.SaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Attaches a loaded document to a fresh edit session and resets transient selection.</summary>
    private void AttachDocument(BuildingDocument document, string? filename)
    {
        _session.Changed -= OnDocumentChanged;
        _session.RenderPhotosChanged -= OnRenderPhotosChanged;
        // Whatever the person decided about the project being replaced, its automatic copy is no longer needed.
        _autosave?.Stop();
        DiscardRecoveryCopy();
        // The untouched single north view identifies projects from before cardinal elevations existed. A user who has
        // deliberately removed views keeps that choice when the project is reopened.
        var elevations = document.Views.Where(view => view.Kind == ProjectViewKind.Elevation).ToArray();
        if (elevations.Length == 0 || elevations is [{ Id: var id, Name: "North Elevation" }] &&
            id == BuiltInProjectViews.NorthElevationId)
            BuiltInProjectViews.EnsureElevations(document.Views);
        BuiltInProjectViews.EnsureAreaSchedule(document.Views);
        BuiltInProjectViews.EnsureLocation(document.Views);
        _session = new DocumentSession(document);
        _session.Changed += OnDocumentChanged;
        _session.RenderPhotosChanged += OnRenderPhotosChanged;
        // The previous project's picker may outlive the async viewport rebuild.
        // Invalidate it synchronously before any new-project interaction is possible.
        _picker = null;
        _activeLevelId = document.Levels[0].Id;
        _pendingWallStart = null;
        _selectedWallId = null;
        _selectedOpeningId = null;
        _selectedWallOffset = null;
        _selectedRoomId = null;
        _selectedFloorId = null;
        _selectedSheetId = null;
        _selectedViewId = null;
        _filePath = filename;
        RenderPanelControl.SetProjectFilePath(_filePath);
        RenderPanelControl.ResetRenderedImageForDocument();
        // Prevent an async sky load from a previous document from leaking into the new project.
        RenderPanelControl.ClearHdriForDocument();
        RenderPanelControl.ClearLutForDocument();
        _dirty = false;
        _projectChosen = true;
        HideStartScreen();
        if (filename is not null) RememberRecent(filename, document);
        _drawingWall = false;
        UpdateWallToolVisualState();
        RestoreLinks(filename);
        RefreshAll();
        FrameCameraToScene();
        if (document.RenderSetup is { } setup) ApplyRenderSetup(setup);
        RenderPanelControl.RestoreMovie();
        RenderPanelControl.RestoreOcean();
    }

    /// <summary>Restores a project's saved render view: the shared camera and the Render panel's settings.</summary>
    private void ApplyRenderSetup(Guanacaste3D.Core.Rendering.RenderSetup setup)
    {
        _navigation.Position = new System.Numerics.Vector3((float)setup.CameraX, (float)setup.CameraY, (float)setup.CameraZ);
        _navigation.YawDegrees = (float)setup.YawDegrees;
        _navigation.PitchDegrees = (float)setup.PitchDegrees;
        _navigation.FieldOfViewDegrees = (float)setup.FieldOfViewDegrees;
        RenderPanelControl.ApplySetupForProject(setup, _filePath);
        UpdateModelCamera();
    }

    /// <summary>
    /// Protects unsaved work before a destructive step. Yes saves first (and continues only if saving worked), No
    /// discards, and Cancel, or closing the dialog with its X button, stays where the user is.
    /// </summary>
    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var answer = MessageBox.Show(this, GetText("Dialog.UnsavedChanges"), "Guanacaste 3D", MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning, MessageBoxResult.Cancel);
        return answer switch
        {
            MessageBoxResult.Yes => SaveProject(false),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    /// <summary>Prevents window closure when the user keeps unsaved work.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
        else
        {
            CancelRender();
            // Closing on purpose, after saving or giving the changes up, leaves no copy behind.
            _autosave?.Stop();
            DiscardRecoveryCopy();
        }
        base.OnClosing(e);
    }

    /// <summary>Closes the application through the normal unsaved-change guard.</summary>
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Restores the previous document snapshot.</summary>
    private void Undo_Click(object sender, RoutedEventArgs e) { _session.Undo(); _selectedWallId = null; RefreshAll(); }

    /// <summary>Reapplies the next document snapshot.</summary>
    private void Redo_Click(object sender, RoutedEventArgs e) { _session.Redo(); _selectedWallId = null; RefreshAll(); }

    /// <summary>Activates selection mode.</summary>
    private void Select_Click(object sender, RoutedEventArgs e) => SetWallTool(false);

    /// <summary>Activates two-point wall placement.</summary>
    private void Wall_Click(object sender, RoutedEventArgs e) => SetWallTool(true);

    /// <summary>Adds a door opening to the selected wall.</summary>
    private void Door_Click(object sender, RoutedEventArgs e) =>
        CreateHostedOpening(OpeningKind.Door);

    /// <summary>Adds a window opening to the selected wall.</summary>
    private void Window_Click(object sender, RoutedEventArgs e) =>
        CreateHostedOpening(OpeningKind.Window);

    /// <summary>Arms a hosted placement preview without mutating the document.</summary>
    private void CreateHostedOpening(OpeningKind kind)
    {
        SetWallTool(false);
        _placingOpening = kind;
        _selectedOpeningId = _selectedWallId = null;
        ViewTabs.SelectedIndex = 0;
        StatusText.Text = GetText("Status.OpeningPlacement");
        RenderPlan();
    }
    /// <summary>Shows the active architectural plan.</summary>
    private void ShowPlan_Click(object sender, RoutedEventArgs e)
    {
        var plan = _session.Document.Views.First(view =>
            view.Kind == ProjectViewKind.Plan && view.LevelId == _activeLevelId);
        SelectProjectView(plan.Id);
    }

    /// <summary>Shows the coordinated three-dimensional model.</summary>
    private void ShowModel_Click(object sender, RoutedEventArgs e) =>
        SelectProjectView(BuiltInProjectViews.Model3DId);

    /// <summary>Displays application version and platform information.</summary>
    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, GetText("Dialog.AboutText"),
            GetText("Dialog.AboutTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>Switches between wall placement and selection while cancelling competing sketches.</summary>
    private void SetWallTool(bool drawing)
    {
        CancelMoveTool();
        CancelExtendedDrawing();
        _selectedViewId = null;
        _drawingWall = drawing;
        _awaitingSimilarWallPick = false;
        _pendingWallStart = null;
        _wallPreviewPoint = null;
        _chainTail = null;
        _alignmentGuides = [];
        if (drawing)
        {
            WallContextTab.IsSelected = true;
            ViewTabs.SelectedIndex = 0;
        }
        UpdateWallToolVisualState();
        StatusText.Text = GetText(drawing ? "Status.WallTool" : "Status.SelectionMode");
        RenderPlan();
    }

}
