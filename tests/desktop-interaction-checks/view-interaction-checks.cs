using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{

    /// <summary>
    /// Clicks an element in the 3D view the way the navigator reports a click: the element becomes the selection (so the plan
    /// and properties follow), is outlined in the model and carries temporary dimension labels; clicking the sky clears it.
    /// </summary>
    private static void Model3DSelectionChecks()
    {
        SetField("_dirty", false);
        // The preceding tests leave a picker for another project. A project switch
        // must discard that cache before the asynchronous viewport rebuild finishes.
        var previousPicker = new Guanacaste3D.Core.Navigation.ElementPicker(Field<DocumentSession>("_session").Document);
        SetField("_picker", previousPicker);
        Invoke("OpenSample_Click", _window, new RoutedEventArgs());
        Check(!ReferenceEquals(Field<object?>("_picker"), previousPicker),
            "Opening another project invalidates the previous 3D element picker immediately");
        var tabs = (TabControl)_window.FindName("ViewTabs");
        tabs.SelectedIndex = 1;
        Pump();
        var surface = (FrameworkElement)_window.FindName("ModelSurface");
        var overlay = (Canvas)_window.FindName("ModelOverlay");
        var camera = Field<Guanacaste3D.Core.Navigation.NavigationCamera>("_navigation");
        var document = Field<DocumentSession>("_session").Document;
        var wall = document.Walls[0];
        var datum = (float)document.Levels.First(level => level.Id == wall.LevelId).Elevation;
        var middle = new System.Numerics.Vector3((float)(wall.Start.X + wall.End.X) / 2, (float)(wall.Start.Y + wall.End.Y) / 2,
            datum + (float)wall.Height / 2);
        var pixel = camera.Project(middle, (float)surface.ActualWidth, (float)surface.ActualHeight);
        Check(pixel is not null, "A wall of the sample is in front of the 3D camera");
        Invoke("HandleViewClick", new Point(pixel!.Value.X, pixel.Value.Y), surface);
        Pump();
        Check(Field<Guid?>("_selectedWallId") is Guid selectedWall && document.Walls.Any(item => item.Id == selectedWall),
            "Clicking an element in the 3D view selects a wall in the active project");
        Check(overlay.Children.Count >= 3, "The selected element shows its temporary dimensions in the 3D view");
        SaveBitmap("artifacts/interaction-evidence/model-3d-selection.png");
        Invoke("HandleViewClick", new Point(2, 2), surface);
        Pump();
        Check(Field<Guid?>("_selectedWallId") is null && overlay.Children.Count == 0, "Clicking empty space clears the 3D selection");

        // With the wall tool armed, two clicks on the ground of the 3D view draw a wall, as two clicks in the plan do.
        var before = Field<DocumentSession>("_session").Document.Walls.Count;
        Invoke("SetWallTool", true);
        foreach (var ground in new[] { new System.Numerics.Vector3(-4, -3, 0), new System.Numerics.Vector3(-1, -3, 0) })
        {
            var at = camera.Project(ground, (float)surface.ActualWidth, (float)surface.ActualHeight);
            Check(at is not null, "The ground point for the new wall is on screen");
            Invoke("HandleViewClick", new Point(at!.Value.X, at.Value.Y), surface);
            Pump();
        }
        Check(Field<DocumentSession>("_session").Document.Walls.Count == before + 1, "Two clicks in the 3D view with the wall tool draw a wall");
        Invoke("SetWallTool", false);
        tabs.SelectedIndex = 0;
        Pump();

        // The plan carries one symbol per elevation; clicking one selects it, and a rename shows on the plan and the tab.
        var symbols = _canvas.Children.OfType<System.Windows.Shapes.Ellipse>()
            .Where(shape => shape.Tag is Guid id && document.Views.Any(view => view.Id == id && view.Kind == ProjectViewKind.Elevation)).ToList();
        Check(symbols.Count == 4, "The plan shows four elevation symbols");
        Invoke("FitPlan_Click", _window, new RoutedEventArgs());
        Pump();
        SaveBitmap("artifacts/interaction-evidence/plan-elevation-symbols.png");
        var north = (Guid)symbols.Select(shape => (Guid)shape.Tag).First(id => id == BuiltInProjectViews.NorthElevationId);
        var clickArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = symbols.First(shape => (Guid)shape.Tag == north)
        };
        Invoke("TrySelectElevationSymbol", clickArgs);
        Check(Field<Guid?>("_selectedViewId") == north, "Clicking an elevation symbol selects its view");
        Field<DocumentSession>("_session").RenameView(north, "Fachada principal");
        Invoke("RefreshAll");
        Invoke("RenderOrthographicViews");
        Pump();
        Check(_canvas.Children.OfType<TextBlock>().Any(label => label.Text == "Fachada principal") &&
              ((TabItem)tabs.Items[4]).Header as string == "Fachada principal", "A renamed elevation is renamed on the plan symbol and the tab");
        Field<DocumentSession>("_session").Undo();
        Invoke("RefreshAll");

        // Double-clicking a door opens its family with an editable plan view and 3D view.
        var door = Field<DocumentSession>("_session").Document.Openings.First(item => item.Kind == OpeningKind.Door);
        var type = typeof(MainWindow).Assembly.GetType("Guanacaste3D.Desktop.OpeningFamilyViewsWindow")!;
        var family = (Window)Activator.CreateInstance(type, Field<DocumentSession>("_session"), door.Id)!;
        family.Owner = _window;
        family.Show();
        Pump();
        Pump();
        var familyViewport = FindVisual<System.Windows.Controls.Viewport3D>(family);
        Check(familyViewport is not null && familyViewport.Children.Count >= 3, "The door family opens with a 3D view of the door in its wall");
        var familyPlan = FindVisual<Canvas>(family, canvas => canvas.Background is not null && canvas.Children.Count > 0);
        Check(familyPlan is not null, "The door family opens with a plan view drawn from its parameters");
        var widthText = door.Width.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
        var widthBox = FindVisual<TextBox>(family, box => box.Text == widthText);
        Check(widthBox is not null, "The door family lists its width as an editable parameter");
        widthBox!.Text = (door.Width + 0.1).ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
        Pump();
        SaveBitmap("artifacts/interaction-evidence/door-family-views.png");
        var familyBitmap = new RenderTargetBitmap((int)family.ActualWidth, (int)family.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        familyBitmap.Render(family);
        using (var stream = File.Create("artifacts/interaction-evidence/door-family-window.png"))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(familyBitmap));
            encoder.Save(stream);
        }
        family.Close();
        SetField("_dirty", false);
    }

    /// <summary>Locates a typed visual descendant to inspect controls that live inside WPF templates.</summary>
    private static T? FindVisual<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found && (match is null || match(found))) return found;
            if (FindVisual(child, match) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>
    /// Exercises the Render tab against the real window: the live preview appears and follows the shared camera, a model
    /// dropped on the window is imported through its dialog, linked, saved beside the project and restored when it is reopened.
    /// </summary>
    private static void RenderPreviewAndImportChecks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // Earlier checks edited the project; mark it clean so opening the sample does not wait for the save prompt.
            SetField("_dirty", false);
            Invoke("OpenSample_Click", _window, new RoutedEventArgs());
            Pump();
            var tabs = (TabControl)_window.FindName("ViewTabs");
            var standardSidebar = (FrameworkElement)_window.FindName("StandardSidebar");
            tabs.SelectedIndex = 6;
            Pump();
            var panel = (Control)_window.FindName("RenderPanelControl");
            var imageHost = (FrameworkElement)panel.FindName("ImageHost");
            var titleBar = (FrameworkElement)_window.FindName("MainTitleBar");
            var ribbon = (FrameworkElement)_window.FindName("RibbonTabControl");
            var globalStatus = (FrameworkElement)_window.FindName("MainStatusStrip");
            var return3D = (Button)_window.FindName("RenderReturn3DButton");
            Check(panel.Visibility == Visibility.Visible && standardSidebar.Visibility == Visibility.Collapsed &&
                  titleBar.Visibility == Visibility.Collapsed && ribbon.Visibility == Visibility.Collapsed &&
                  globalStatus.Visibility == Visibility.Collapsed && return3D.Visibility == Visibility.Visible,
                "Immersive Render hides the BIM chrome and palettes while leaving a visible 3D return");
            Check(Math.Abs(imageHost.TranslatePoint(new Point(), panel).X - 375) < 4,
                "The Render studio keeps a compact resizable sidebar and a separate mode rail");
            Check(panel.Background is SolidColorBrush { Color: { R: < 60, G: < 60, B: < 70 } },
                "The independent Render studio uses a dark photographic theme");
            var sectionTabs = (TabControl)panel.FindName("SectionTabs");
            var materialsButton = (Button)panel.FindName("RenderRailMaterials");
            materialsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(sectionTabs.SelectedIndex == 3, "The dedicated Render rail opens the materials panel");
            static IEnumerable<object> LogicalDescendants(DependencyObject parent) =>
                LogicalTreeHelper.GetChildren(parent).OfType<object>().SelectMany(child =>
                    child is DependencyObject node ? new[] { child }.Concat(LogicalDescendants(node)) : new[] { child });
            var materialRows = (StackPanel)panel.FindName("MaterialRows");
            var faceButton = FindButtons(materialRows)
                .First(button => Equals(button.ToolTip, "Elegir el material con bolas de ejemplo"));
            faceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var gridPopup = LogicalDescendants(materialRows).OfType<System.Windows.Controls.Primitives.Popup>().First(item => item.IsOpen);
            var ballGrid = (WrapPanel)((ScrollViewer)((StackPanel)((Border)gridPopup.Child).Child).Children[2]).Content;
            Check(ballGrid.Children.OfType<Button>().Count() > 20,
                "A surface's material picker opens a grid of material balls instead of a text list");
            var allBalls = ballGrid.Children.OfType<Button>().Count();
            var chipRow = (WrapPanel)((StackPanel)((Border)gridPopup.Child).Child).Children[1];
            chipRow.Children.OfType<Button>().First(chip => Equals(chip.Content, "Metales"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var metalBalls = ((WrapPanel)((ScrollViewer)((StackPanel)((Border)gridPopup.Child).Child).Children[2]).Content)
                .Children.OfType<Button>().Count();
            Check(metalBalls > 5 && metalBalls < allBalls,
                "A family chip narrows the ball grid to that family's materials");
            gridPopup.IsOpen = false;
            var gallery = (Expander)panel.FindName("GalleryExpander");
            gallery.IsExpanded = true;
            Pump();
            var tiles = (WrapPanel)panel.FindName("GalleryTiles");
            var filter = (ComboBox)panel.FindName("GalleryGroupPicker");
            var target = (ComboBox)panel.FindName("GalleryTargetPicker");
            var search = (TextBox)panel.FindName("GallerySearch");
            ComboBoxItem GroupItem(string tag) => filter.Items.OfType<ComboBoxItem>().First(item => item.Tag as string == tag);
            Check(filter.SelectedItem == GroupItem("presets") && tiles.Children.OfType<Button>().Any(),
                "The material library opens expanded on the ready-made materials, each shown as a ball");
            filter.SelectedItem = GroupItem("trees");
            Pump();
            Check(tiles.Children.OfType<Button>().Count() == 8 &&
                  ((TextBlock)panel.FindName("GalleryPageText")).Text == "1 / 3",
                "The optional nature gallery shows only eight thumbnail cards per page");
            Check(target.SelectedItem is ComboBoxItem { Tag: SurfaceKind.Bark },
                "The gallery initially targets tree bark instead of an unrelated BIM surface");
            ((Button)panel.FindName("GalleryNextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(((TextBlock)panel.FindName("GalleryPageText")).Text == "2 / 3" &&
                  tiles.Children.OfType<Button>().Count() == 8,
                "Next page reveals another eight thumbnails without mounting the entire texture collection");
            ((Button)panel.FindName("GalleryPreviousButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            search.Text = "guanacaste";
            Pump();
            Check(tiles.Children.OfType<Button>().Count() == 2,
                "Accent-insensitive gallery search filters tree bark and foliage simultaneously");
            filter.SelectedItem = GroupItem("bark");
            Pump();
            Check(tiles.Children.OfType<Button>().Count() == 1,
                "Bark filter narrows the gallery without decoding all image previews");
            tiles.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(Field<DocumentSession>("_session").Document.RenderMaterials.Any(material =>
                    material.Category == SurfaceKind.Bark &&
                    material.TexturePath == "builtin:tree-bark-guanacaste"),
                "Clicking a thumbnail assigns its stable built-in texture key through the real project material transaction");

            // The big Lumion-style library: lit balls by category, named, applied with one click.
            target.SelectedItem = target.Items.OfType<ComboBoxItem>().First(item => item.Tag is SurfaceKind.Metal);
            ((Button)panel.FindName("MaterialPickerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var picker = Application.Current.Windows.Cast<Window>().FirstOrDefault(window => window.GetType().Name == "MaterialPickerWindow");
            Check(picker is not null, "The materials button opens the big library window");
            if (picker is not null)
            {
                static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
                {
                    for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
                    {
                        var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                        if (child is T match) yield return match;
                        foreach (var inner in Descendants<T>(child)) yield return inner;
                    }
                }
                Pump();
                var grid = Descendants<WrapPanel>(picker).First(wrap => wrap.Children.Count > 20);
                var names = grid.Children.OfType<Border>().SelectMany(tile => Descendants<TextBlock>(tile)).Select(text => text.Text).ToList();
                Check(names.Contains("Acero") && names.Contains("Caucho negro") && names.Contains("Fibra de carbono"),
                    "The library lists metals, rubbers and plastics by name under each ball");
                var steel = grid.Children.OfType<Border>().First(tile => Descendants<TextBlock>(tile).Any(text => text.Text == "Acero"));
                steel.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                Pump();
                Check(Field<DocumentSession>("_session").Document.RenderMaterials.Any(material => material.TexturePath == "material:metal-steel"),
                    "One click on a ball applies that material to the chosen surface");
                picker.Close();
            }
            target.SelectedItem = target.Items.OfType<ComboBoxItem>().First(item => item.Tag is SurfaceKind.Bark);

            // A referenced .g3dm appears as a picture in the same gallery, without a filesystem scan.
            var libraryType = typeof(MainWindow).Assembly.GetType("Guanacaste3D.Desktop.RenderTextureLibrary")!;
            var sourceImage = (string)libraryType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, ["builtin:brick-red", null])!;
            var pbrApi = typeof(MainWindow).Assembly.GetType("Guanacaste3D.Desktop.PbrMaterialPackages")!;
            var createPackage = pbrApi.GetMethod("CreateFromImage", BindingFlags.Public | BindingFlags.Static)!;
            var packagePath = Path.Combine(folder, "galeria-pbr.g3dm");
            createPackage.Invoke(null, [
                sourceImage, packagePath, "Piedra Galería PBR", 0.76, 0.08, 1.5, 0.3
            ]);
            Field<DocumentSession>("_session").SetRenderMaterial(
                new RenderMaterialAssignment(SurfaceKind.Bark, "pbr:" + packagePath)
                { TextureSize = 1.5, Relief = 0.3 });
            Invoke("RefreshAll");
            Pump();
            filter.SelectedItem = GroupItem("pbr");
            search.Text = "Piedra Galeria PBR";
            Pump();
            Check(tiles.Children.OfType<Button>().Count() == 1 &&
                  tiles.Children.OfType<Button>().Single().Tag is string cardKey &&
                  cardKey.StartsWith("pbr:", StringComparison.Ordinal),
                "Project-referenced .g3dm materials have a searchable image card and are not treated as raw file paths");
            Check(((WrapPanel)panel.FindName("GalleryPbrMapCards")).Children.Count == 4,
                "PBR inspector displays editable normal, roughness and metallic maps plus read-only package height");
            var roughnessControl = (Slider)panel.FindName("GalleryRoughnessSlider");
            var metallicControl = (Slider)panel.FindName("GalleryMetallicSlider");
            var reliefControl = (Slider)panel.FindName("GalleryReliefSlider");
            Check(Math.Abs(roughnessControl.Value - 0.76) < 0.001 &&
                  Math.Abs(metallicControl.Value - 0.08) < 0.001,
                "The inspector reads the physical defaults from the imported .g3dm manifest");
            roughnessControl.Value = 0.28;
            metallicControl.Value = 0.47;
            reliefControl.Value = 0.63;
            ((Button)panel.FindName("GalleryApplyAppearanceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var pbrApplied = Field<DocumentSession>("_session").Document.RenderMaterials.Single(material =>
                material.Category == SurfaceKind.Bark && material.ElementId is null);
            Check(pbrApplied.RoughnessOverride is { } rr && Math.Abs(rr - 0.28) < 0.001 &&
                  pbrApplied.MetallicOverride is { } mm && Math.Abs(mm - 0.47) < 0.001 &&
                  Math.Abs(pbrApplied.Relief - 0.63) < 0.001,
                "Sliders commit one non-destructive, project-scoped material appearance edit");
            var persistenceFile = Path.Combine(folder, "apariencia-pbr.g3d");
            ProjectFile.Save(persistenceFile, Field<DocumentSession>("_session").Document);
            Check(ProjectFile.Load(persistenceFile).RenderMaterials.Any(material =>
                    material.Category == SurfaceKind.Bark && material.RoughnessOverride is > 0.27 and < 0.29),
                "The material appearance sliders survive native save and reload");
            Check(File.ReadAllText(packagePath).Contains("0.76") && !File.ReadAllText(packagePath).Contains("0.28"),
                "Editing one surface leaves the shared original .g3dm material unchanged");
            ((Button)panel.FindName("GalleryResetAppearanceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var restored = Field<DocumentSession>("_session").Document.RenderMaterials.Single(material =>
                material.Category == SurfaceKind.Bark && material.ElementId is null);
            Check(restored.RoughnessOverride is null && restored.MetallicOverride is null &&
                  Math.Abs(restored.Relief - 0.3) < 0.001,
                "Reset removes only per-project overrides and restores the package relief");
            gallery.IsExpanded = false;
            ((Button)panel.FindName("RenderRailScene")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(sectionTabs.SelectedIndex == 0, "The Render rail can return to the environment panel");
            var viewport = (Viewport3D)panel.FindName("InteractiveViewport");
            Check(WaitFor(() => viewport.Children.Count > 2 && viewport.Camera is not null, 60),
                "The Render tab displays the project as interactive 3D geometry");
            Check(typeof(RenderPanel).GetField("_preview", PrivateInstance)!.GetValue(panel) is null,
                "Opening Render does not start the progressive preview worker");
            var preview = (System.Windows.Controls.Primitives.ToggleButton)panel.FindName("PreviewToggle");
            preview.IsChecked = true;
            var previewImage = (Image)panel.FindName("PreviewImage");
            Check(WaitFor(() => previewImage.Source is WriteableBitmap, 60),
                "The preview button renders the current camera on demand");
            preview.IsChecked = false;
            Check(viewport.Visibility == Visibility.Visible, "Leaving preview restores the interactive model");
            SaveBitmap("artifacts/interaction-evidence/render-workspace-dark.png");
            var before = ((PerspectiveCamera)viewport.Camera!).Position;
            var camera = Field<Guanacaste3D.Core.Navigation.NavigationCamera>("_navigation");
            camera.Position = new System.Numerics.Vector3(-8, 8, 3);
            camera.LookAt(new System.Numerics.Vector3(0, 0, 1));
            var navigator = panel.GetType().GetProperty("Navigator", PrivateInstance | System.Reflection.BindingFlags.Public)!.GetValue(panel)!;
            ((Action?)navigator.GetType().GetField("Changed", PrivateInstance)!.GetValue(navigator))?.Invoke();
            Check(((PerspectiveCamera)viewport.Camera!).Position != before, "Moving the shared camera updates the interactive viewport");
            var orbitButton = (Button)panel.FindName("OrbitRightButton");
            var beforeOrbit = ((PerspectiveCamera)viewport.Camera!).Position;
            orbitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((PerspectiveCamera)viewport.Camera!).Position != beforeOrbit,
                "The visible orbit control rotates the actual interactive camera");
            var beforeWheel = camera.Position;
            imageHost.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            {
                RoutedEvent = UIElement.MouseWheelEvent
            });
            Check(camera.Position != beforeWheel, "A wheel event on the Render surface moves the shared camera");
            var navigatorType = navigator.GetType();
            navigatorType.GetField("_dragButton", PrivateInstance)!.SetValue(navigator, MouseButton.Middle);
            navigatorType.GetField("_pressPoint", PrivateInstance)!.SetValue(navigator, new Point(100, 100));
            navigatorType.GetField("_last", PrivateInstance)!.SetValue(navigator, new Point(100, 100));
            navigatorType.GetField("_dragged", PrivateInstance)!.SetValue(navigator, false);
            var beforeDrag = camera.Position;
            navigatorType.GetMethod("ProcessPointerMove", PrivateInstance)!.Invoke(navigator,
                [new Point(150, 120), ModifierKeys.Shift]);
            Check(camera.Position != beforeDrag, "Shift and middle-button dragging orbits the Render camera");
            navigatorType.GetField("_dragButton", PrivateInstance)!.SetValue(navigator, null);
            typeof(RenderPanel).GetField("_showingFinal", PrivateInstance)!.SetValue(panel, true);
            typeof(RenderPanel).GetField("_rendering", PrivateInstance)!.SetValue(panel, true);
            typeof(RenderPanel).GetMethod("ShowInteractiveScene", PrivateInstance)!.Invoke(panel, [false]);
            orbitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(viewport.Visibility == Visibility.Visible &&
                  !(bool)typeof(RenderPanel).GetField("_showingFinal", PrivateInstance)!.GetValue(panel)!,
                "Navigating during a render reveals the model without stopping the render job");
            typeof(RenderPanel).GetField("_rendering", PrivateInstance)!.SetValue(panel, false);
            var showImageButton = (Button)panel.FindName("ShowImageButton");
            showImageButton.IsEnabled = true;
            typeof(RenderPanel).GetMethod("ResetRenderedImageForDocument", PrivateInstance)!.Invoke(panel, null);
            Check(!showImageButton.IsEnabled && viewport.Visibility == Visibility.Visible,
                "Opening another project clears the previous render image and returns to 3D navigation");
            camera.Position = new System.Numerics.Vector3(8.5f, -6, 1.5f);
            camera.LookAt(new System.Numerics.Vector3(8.5f, 0, 1.5f));
            panel.GetType().GetMethod("OnCameraChanged", PrivateInstance)!.Invoke(panel, null);
            var entryType = panel.GetType().GetNestedType("GalleryEntry", BindingFlags.NonPublic)!;
            var textureKey = (string)tiles.Children.OfType<Button>().First().Tag;
            var materialEntry = Activator.CreateInstance(entryType, textureKey, "Test", 1d, 0.3d, false, 0.82d, 0d)!;
            var applyDrop = panel.GetType().GetMethod("ApplyDroppedMaterial", PrivateInstance)!;
            var dropped = (bool)applyDrop.Invoke(panel, [materialEntry, new Point(imageHost.ActualWidth / 2, imageHost.ActualHeight / 2)])!;
            Check(dropped && Field<DocumentSession>("_session").Document.RenderMaterials.Any(item => item.ElementId is not null && item.TexturePath == textureKey),
                "Dropping a gallery material on a wall assigns it specifically to that element");
            Field<DocumentSession>("_session").Undo();
            Check(!Field<DocumentSession>("_session").Document.RenderMaterials.Any(item => item.ElementId is not null && item.TexturePath == textureKey),
                "One undo removes the dropped material assignment");

            // A mesh dropped on the window: the import dialog is accepted automatically, as the user would press Import.
            var model = Path.Combine(folder, "tree.obj");
            File.WriteAllLines(model, ["v 0 0 0", "v 2 0 0", "v 0 2 0", "v 0 0 3", "f 1 2 3", "f 1 3 4", "f 1 4 2", "f 2 4 3"]);
            var accept = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            accept.Tick += (_, _) =>
            {
                foreach (var dialog in Application.Current.Windows.OfType<ImportWindow>().Where(window => window.IsVisible))
                {
                    var button = (Button)dialog.FindName("ImportButton");
                    if (button.IsEnabled && button.Content is string text && text != "Cerrar")
                        dialog.GetType().GetMethod("Import_Click", PrivateInstance)!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
                    else if (button.Content is "Cerrar") dialog.DialogResult = true;
                }
            };
            accept.Start();
            return3D.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check(tabs.SelectedIndex == 1 &&
                  standardSidebar.Visibility == Visibility.Visible && panel.Visibility == Visibility.Collapsed &&
                  titleBar.Visibility == Visibility.Visible && ribbon.Visibility == Visibility.Visible &&
                  globalStatus.Visibility == Visibility.Visible && return3D.Visibility == Visibility.Collapsed,
                "The 3D button restores the coordinated model with every BIM control, without recreating the document");
            var import = (Task)Invoke("ImportFilesAsync", (object)new[] { model })!;
            Check(WaitFor(() => import.IsCompleted, 60), "Importing a dropped file finishes");
            accept.Stop();
            var links = Field<object>("_links");
            var list = (System.Collections.IList)links.GetType().GetProperty("Links")!.GetValue(links)!;
            Check(list.Count == 1, "The dropped mesh is linked to the project");
            Check(Field<bool>("_dirty"), "Linking a model marks the project as changed");

            var path = Path.Combine(folder, "casa.g3d");
            SetField("_filePath", path);
            Check((bool)Invoke("SaveProject", false)!, "The project saves with a linked model");
            Check(File.Exists(path + ".links.json"), "Saving writes the links file beside the project");
            Invoke("OpenProjectPath", path);
            Check(WaitFor(() =>
            {
                links = Field<object>("_links");
                var reopened = (System.Collections.IList)links.GetType().GetProperty("Links")!.GetValue(links)!;
                var loaded = reopened.Count == 1 && (bool)links.GetType().GetMethod("IsLoaded")!.Invoke(links, [((Guanacaste3D.Core.Import.ModelLink)reopened[0]!).Id])!;
                return loaded;
            }, 30), "Reopening the project restores and reloads its linked model");
        }
        finally
        {
            SetField("_dirty", false);
            Directory.Delete(folder, recursive: true);
        }
    }
}
