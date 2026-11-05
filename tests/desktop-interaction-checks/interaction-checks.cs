using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

/// <summary>Runs real WPF controls and routed events in an isolated test window; never opens user documents.</summary>
internal static partial class InteractionChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static MainWindow _window = null!;
    private static Canvas _canvas = null!;
    private static Point2 _lastPointer;

    /// <summary>Builds a throwaway project, exercises controls, writes a real WPF bitmap, and exits.</summary>
    [STAThread]
    private static int Main()
    {
        // Copies and error logs go to a throwaway folder: the installed program offers whatever it finds in the real one.
        var scratch = Path.Combine(Path.GetTempPath(), "g3d-ui-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("G3D_RECOVERY_DIR", Path.Combine(scratch, "recovery"));
        Environment.SetEnvironmentVariable("G3D_LOG_DIR", Path.Combine(scratch, "logs"));
        Environment.SetEnvironmentVariable("G3D_NO_EXPLORER", "1");
        var application = new App();
        application.InitializeComponent();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // G3D_ONLY=cobogo runs just the checks on the breeze-block house, which is what a quick loop needs.
        if (Environment.GetEnvironmentVariable("G3D_ONLY") == "cobogo")
        {
            CobogoHouseChecks();
            Console.WriteLine("Desktop interaction checks passed (cobogó house only).");
            return 0;
        }
        // G3D_ONLY=movie makes a movie of the house through the Render panel's own code (see MovieJob).
        if (Environment.GetEnvironmentVariable("G3D_ONLY") == "movie")
        {
            MovieJob();
            return 0;
        }
        // G3D_ONLY=render runs just the Render tab on the cobogó house sample (the render, movie and presentation checks).
        if (Environment.GetEnvironmentVariable("G3D_ONLY") == "render")
        {
            StartupSampleChecks();
            Console.WriteLine("Desktop interaction checks passed (render tab only).");
            return 0;
        }
        _window = new MainWindow { Width = 1440, Height = 1000, WindowState = WindowState.Normal };
        try
        {
            _window.Show();
            _window.Activate();
            Pump();
            _canvas = (Canvas)_window.FindName("PlanCanvas");
            var session = Field<DocumentSession>("_session");
            var level = session.Document.Levels[0].Id;
            var basicTypeIndex = session.Document.WallTypes.FindIndex(type => type.Id == BuiltInWallTypes.Basic200Id);
            session.Document.WallTypes[basicTypeIndex] = session.Document.WallTypes[basicTypeIndex] with
            {
                LeftRepelloThickness = 0.015,
                LeftFineThickness = 0.003,
                RightRepelloThickness = 0.015,
                RightFineThickness = 0.003
            };
            var wallId = session.AddWall(level, new(-6, 0), new(6, 0));
            session.AddWall(level, new(-6, 0), new(-6, 4));
            Invoke("Door_Click", _window, new RoutedEventArgs());
            Check(session.Document.Openings.Count == 0, "Door command arms placement without insertion");
            Move(new(-2.55, 0));
            Check(session.Document.Openings.Count == 0 && _canvas.Children.Cast<UIElement>().Any(item => item.Opacity == 0.65),
                "Pointer movement renders a ghost without modifying the document");
            ClickPlan();
            Check(session.Document.Openings.Count == 1, "Placement transaction commits the hosted preview");
            var door = session.Document.Openings.Single();
            Move(new(-2.55, 0));
            ClickPlan();
            Check(session.Document.Openings.Count == 1, "Overlapping placement is rejected");
            Invoke("Window_Click", _window, new RoutedEventArgs());
            Check(session.Document.Openings.Count == 1, "Window command also avoids immediate insertion");
            Move(new(3, 0));
            ClickPlan();
            Check(session.Document.Openings.Count == 2, "Window placement commits to the resolved wall");
            Invoke("SetWallTool", false);
            SetField("_selectedOpeningId", (Guid?)door.Id);
            SetField("_selectedWallId", (Guid?)wallId);
            Invoke("RefreshProperties");
            var wallProperties = ((TextBlock)_window.FindName("PropertiesText")).Text;
            Check(wallProperties.Contains("CANTIDADES NETAS POR CAPA", StringComparison.Ordinal) &&
                  wallProperties.Contains("Núcleo:", StringComparison.Ordinal) &&
                  wallProperties.Contains("Repello total:", StringComparison.Ordinal) &&
                  wallProperties.Contains("Fino total:", StringComparison.Ordinal) &&
                  !wallProperties.Contains("Repello total: 0.00", StringComparison.Ordinal) &&
                  !wallProperties.Contains("Fino total: 0.00", StringComparison.Ordinal),
                "Selecting a wall exposes core, repello and fine-layer quantities in the property palette");
            SaveBitmap("artifacts/interaction-evidence/wall-layer-quantities.png");
            Invoke("RenderPlan");
            Pump();
            var editor = _canvas.Children.OfType<Button>().First(button =>
                button.Tag is PlanDimension { Kind: PlanDimensionKind.OpeningStart } && button.Content!.ToString()!.Contains('m'));
            editor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var inline = _canvas.Children.OfType<TextBox>().Single(box => box.Tag is PlanDimension { Kind: PlanDimensionKind.OpeningStart });
            Check(inline.IsKeyboardFocusWithin && session.Document.Openings.Single(item => item.Id == door.Id).Begin < 5,
                "Clicking the number opens a focused in-place editor without moving the door yet");
            PressKey(inline, Key.Escape);
            Check(!_canvas.Children.OfType<TextBox>().Any(box => box.Tag is PlanDimension),
                "Escape closes the in-place editor and leaves the door where it was");
            editor = _canvas.Children.OfType<Button>().First(button =>
                button.Tag is PlanDimension { Kind: PlanDimensionKind.OpeningStart } && button.Content!.ToString()!.Contains('m'));
            editor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            inline = _canvas.Children.OfType<TextBox>().Single(box => box.Tag is PlanDimension);
            inline.Text = "5 m";
            PressKey(inline, Key.Enter);
            Check(Math.Abs(session.Document.Openings.Single(item => item.Id == door.Id).Begin - 5) < 1e-9,
                "Clicking the rendered number and entering five meters moves the door exactly");
            var arrow = _canvas.Children.OfType<Button>().First(button =>
                button.Tag is PlanDimension { Kind: PlanDimensionKind.OpeningStart } && button.Content!.ToString() == "↗");
            arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(session.Document.Dimensions.Count == 1, "Promotion arrow creates one persistent annotation");
            Invoke("SetWallTool", false);
            SetField("_selectedOpeningId", null);
            SetField("_selectedWallId", null);
            Invoke("RenderPlan");
            Check(_canvas.Children.OfType<Button>().Any(button => button.Tag is PlanDimension dimension && dimension.Id != Guid.Empty),
                "Permanent annotation remains visible after selection clears");
            Invoke("Column_Click", _window, new RoutedEventArgs());
            Move(new(0, 3));
            Check(session.Document.Columns.Count == 0, "Column preview does not create a column");

            Invoke("SetWallTool", false);
            Invoke("RenderPlan");
            Pump();
            Directory.CreateDirectory("artifacts/interaction-evidence");
            SetField("_scale", 60.0);
            Invoke("RenderPlan");
            Pump();
            SaveBitmap("artifacts/interaction-evidence/plan-dimensions.png");
            ProjectFile.Save("artifacts/interaction-evidence/placed-elements.g3d", session.Document);
            Check(ProjectFile.Load("artifacts/interaction-evidence/placed-elements.g3d").Dimensions.Count == 1,
                "UI-created annotation survives native save and reopen");
            WallLoopClosingChecks(session);
            WallEditCommandChecks(session);
            RotateRibbonChecks(session);
            RandomizedRibbonChecks(session);
            PlantingChecks(session);
            StairToolChecks(session);
            TexturedModelChecks(session);
            MiddleClickViewChecks();
            ElectricalToolChecks(session);
            PlumbingToolChecks(session);
            PlanServiceVisibilityChecks(session);
            CaptureDemoToolTip();
            PhotoThumbnailCacheChecks();
            ProjectThumbnailBudgetChecks();
            IconChecks();
            RenderJoinScene();
            Model3DSelectionChecks();
            RenderPreviewAndImportChecks();
            RevitProjectChecks();
            TileViewChecks();
            CheckAssociatedDocumentStartup();
            MassPlacementChecks();
            ScheduleExportChecks();
            StartupSampleChecks();
            CobogoHouseChecks();
            Console.WriteLine("Desktop interaction checks passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory("artifacts/interaction-evidence");
            SaveBitmap("artifacts/interaction-evidence/failure.png");
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            SetField("_dirty", false);
            _window.Close();
            application.Shutdown();
        }
    }


    /// <summary>Wall-layer and roof exports must remain accessible without a saved schedule.</summary>
    private static void ScheduleExportChecks()
    {
        var emptySession = new DocumentSession(BuildingDocument.Create());
        var window = new SchedulesWindow(emptySession) { Owner = _window };
        try
        {
            window.Show();
            DrainToIdle();
            var wallButton = (Button)window.FindName("ExportWallLayerQuantitiesButton");
            var summaryButton = (Button)window.FindName("ExportWallLayerSummaryButton");
            var revealsButton = (Button)window.FindName("ExportWallOpeningRevealsButton");
            var revealSummaryButton = (Button)window.FindName("ExportWallOpeningSummaryButton");
            var roofButton = (Button)window.FindName("ExportRoofQuantitiesButton");
            Check(wallButton.IsEnabled && summaryButton.IsEnabled && revealsButton.IsEnabled && revealSummaryButton.IsEnabled && roofButton.IsEnabled,
                "Independent quantity exports stay enabled without saved schedule definitions");
            wallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DrainToIdle();
            var status = (TextBlock)window.FindName("StatusText");
            Check(!string.IsNullOrWhiteSpace(status.Text),
                "Wall layer export reports an empty project without opening a save dialog");
            status.Text = string.Empty;
            summaryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DrainToIdle();
            Check(!string.IsNullOrWhiteSpace(status.Text),
                "Wall layer summary reports an empty project without opening a save dialog");
            status.Text = string.Empty;
            revealsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DrainToIdle();
            Check(!string.IsNullOrWhiteSpace(status.Text),
                "Opening reveal export reports missing openings without opening a save dialog");
            status.Text = string.Empty;
            revealSummaryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DrainToIdle();
            Check(!string.IsNullOrWhiteSpace(status.Text),
                "Opening reveal summary reports missing openings without opening a save dialog");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Routes a real key-down event through the target control, as the keyboard would.</summary>
    private static void PressKey(UIElement target, Key key)
    {
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
            { RoutedEvent = Keyboard.KeyDownEvent });
        Pump();
    }

    /// <summary>Injects model coordinates into preview state; OS pointer automation is deliberately excluded.</summary>
    private static void Move(Point2 world)
    {
        _lastPointer = world;
        SetField("_placementPointer", (Point2?)world);
        Invoke("RenderPlan");
        Pump();
    }

    /// <summary>Calls the same placement transaction used by the mouse handler with deterministic coordinates.</summary>
    private static void ClickPlan()
    {
        Invoke("CommitOpeningPlacement", _lastPointer);
        Pump();
    }

    /// <summary>
    /// Flushes layout and queued WPF input. A fixed timer alone races with layout and render work on a loaded machine,
    /// so the dispatcher is drained to idle before and after the timer, which keeps the checks deterministic.
    /// </summary>
    private static void Pump()
    {
        _window.UpdateLayout();
        DrainToIdle();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        DrainToIdle();
    }

    /// <summary>Runs every queued dispatcher item, including layout and rendering, before returning.</summary>
    private static void DrainToIdle()
    {
        for (var pass = 0; pass < 3; pass++)
            _window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
        _window.UpdateLayout();
    }

    /// <summary>Captures the actual composed WPF window for visual review.</summary>
    private static void SaveBitmap(string path)
    {
        var bitmap = new RenderTargetBitmap((int)_window.ActualWidth, (int)_window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Invokes an existing UI handler without introducing a production automation API.</summary>
    private static object? Invoke(string name, params object[] arguments) =>
        typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(_window, arguments);

    /// <summary>Reads isolated test state from the actual window instance.</summary>
    private static T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(_window)!;

    /// <summary>Sets selection or teardown state without editing any user document.</summary>
    private static void SetField(string name, object? value) => typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(_window, value);

    private static readonly System.Diagnostics.Stopwatch _checkClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Fails immediately when an observable UI behavior differs from the contract.</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        // The time since the last check, when it is long: the steps that make the window wait show up here.
        var elapsed = _checkClock.ElapsedMilliseconds;
        _checkClock.Restart();
        if (elapsed >= 700) Console.WriteLine($"SLOW {elapsed} ms: {message}");
        Console.WriteLine("OK: " + message);
    }
}
