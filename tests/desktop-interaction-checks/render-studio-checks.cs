using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{
    private static void RenderStudioChecks()
    {
        Directory.CreateDirectory("artifacts/studio-checks");
        _window = new MainWindow { Width = 1440, Height = 900, WindowState = WindowState.Normal };
        try
        {
            _window.EnterRenderWorkspace();
            _window.Show();
            Pump();
            var panel = (RenderPanel)_window.FindName("RenderPanelControl");
            T Control<T>(string name) => (T)panel.FindName(name);
            object? Call(string name, params object[] args) => typeof(RenderPanel).GetMethod(name, PrivateInstance)!.Invoke(panel, args);
            void Click(string name) => Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var source = (IRenderSource)typeof(RenderPanel).GetField("_source", PrivateInstance)!.GetValue(panel)!;
            Check(Control<Border>("StudioWelcome").IsVisible && Control<Button>("StudioOpenButton").IsVisible &&
                  Control<Button>("StudioSaveButton").IsVisible, "Empty render studio exposes open/save and onboarding");
            Check(Control<TabControl>("SectionTabs").SelectedIndex == 0 && Control<TabControl>("SectionTabs").SelectedContent is not null,
                "Environment settings are selected on first launch");
            SaveBitmap("artifacts/studio-checks/01-start.png");
            Check(!Control<Button>("DuplicateObjectButton").IsEnabled && !Control<Button>("DeleteObjectButton").IsEnabled &&
                  !Control<Button>("UndoSceneButton").IsEnabled && !Control<Button>("RedoSceneButton").IsEnabled,
                "Empty studio disables selection and history actions");
            Click("StudioBuildModeButton");
            Check(Control<Border>("PhotoStrip").Visibility == Visibility.Collapsed, "Scene mode frees the viewport from the photo strip");
            Click("StudioPhotoModeButton");
            Check(Control<Border>("PhotoStrip").Visibility == Visibility.Visible, "Photo mode restores saved camera slots");
            Click("ViewToolsTab");
            Check(Control<Button>("TheaterButton").IsVisible && !Control<Button>("DuplicateObjectButton").IsVisible,
                "View group exposes navigation without unrelated editing tools");
            Click("TheaterButton");
            Check(((ColumnDefinition)panel.FindName("SettingsColumn")).Width.Value == 0 &&
                  Control<Border>("SceneToolsBar").Margin.Bottom == 76, "Theater frees sidebar and photo-strip space");
            Click("TheaterButton");
            Check(((ColumnDefinition)panel.FindName("SettingsColumn")).Width.Value > 0 && Control<Border>("PhotoStrip").IsVisible,
                "Theater button restores panels and the active output mode");
            Click("FxBarButton");
            Check(Control<Border>("FxPicker").IsVisible, "Effects toolbar button opens the real effect library");
            Call("FxPickerClose_Click", panel, new RoutedEventArgs());
            Check(!Control<Border>("FxPicker").IsVisible, "Effect library close returns to the workspace");
            Click("FxBarButton");
            Control<TextBox>("FxSearch").Text = "a";
            var effects = Control<System.Windows.Controls.Primitives.UniformGrid>("FxTiles");
            Check(effects.Children.Count > 0, "Effect search loads thumbnails across categories in the standalone host");
            effects.Children.OfType<Button>().First(x => x.IsEnabled).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!Control<Border>("FxPicker").IsVisible && Control<TabControl>("SectionTabs").SelectedIndex == 1,
                "Available effect tile opens its actual controls");
            Click("StudioSampleButton");
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while ((Control<Viewport3D>("InteractiveViewport").Children.Count <= 2 || Control<Border>("StudioWelcome").IsVisible)
                   && DateTime.UtcNow < deadline) Pump();
            Check(source.HasScene && !Control<Border>("StudioWelcome").IsVisible && Control<Viewport3D>("InteractiveViewport").Children.Count > 2,
                "Bundled sample opens from the real button and becomes navigable");
            SaveBitmap("artifacts/studio-checks/02-scene.png");
            Check(!Control<Border>("InteractiveHintCard").IsVisible, "Navigation help stays out of the scene by default");
            Click("NavigationHelpButton");
            Check(Control<Border>("InteractiveHintCard").IsVisible, "Navigation help opens on request");
            Click("NavigationHelpButton");
            Click("EditToolsTab");
            Click("SceneInspectorButton");
            Pump();
            var items = source.SceneItems();
            Check(items.Count > 10 && Control<StackPanel>("SceneItemsPanel").Children.Count > 0, "Inspector lists actual document objects");
            var search = Control<TextBox>("SceneSearch");
            search.Text = "no-such-object-9471";
            Check(Control<StackPanel>("SceneItemsPanel").Children.Count == 0 && Control<TextBlock>("SceneSummary").Text.Contains("Sin resultados"),
                "Inspector search has a truthful empty state");
            search.Text = "Muro 001";
            Check(Control<StackPanel>("SceneItemsPanel").Children.Count == 1, "Inspector searches native objects by name");
            var target = items.First(x => x.Name == "Muro 001");
            source.SelectSceneItem(target.Id);
            Check(Field<Guid?>("_selectedWallId") == target.Id, "Inspector selection reaches the shared model selection");
            source.SetSceneItemVisible(target.Id, false);
            Check(!source.SceneItems().Single(x => x.Id == target.Id).Visible, "Inspector hides geometry through persisted view visibility");
            var session = Field<DocumentSession>("_session");
            var path = Path.GetFullPath("artifacts/studio-checks/visibility.g3d");
            ProjectFile.Save(path, session.Document);
            Check(ProjectFile.Load(path).Views.Any(x => x.HiddenElementIds.Contains(target.Id)), "Visibility survives project save and reload");
            session.Undo();
            Check(source.SceneItems().Single(x => x.Id == target.Id).Visible, "Scene visibility is undoable");
            var objectBefore = session.Document.StructuralElements.First(x => x.Kind == StructuralElementKind.Component);
            var opening = session.Document.Openings.First();
            source.SelectSceneItem(opening.Id);
            var hostBefore = session.Document.Walls.Single(x => x.Id == opening.WallId);
            Check(!source.CanExecuteSceneCommand("rotate") && !source.CanExecuteSceneCommand("duplicate"),
                "Selecting an opening never exposes actions for its host wall");
            source.ExecuteSceneCommand("rotate");
            source.ExecuteSceneCommand("duplicate");
            Check(session.Document.Walls.Single(x => x.Id == opening.WallId) == hostBefore,
                "Unsupported opening commands leave the host unchanged");
            source.SelectSceneItem(objectBefore.Id);
            var countBefore = session.Document.StructuralElements.Count;
            Click("DuplicateObjectButton");
            var duplicatedId = source.SelectedSceneItemId!.Value;
            Check(session.Document.StructuralElements.Count == countBefore + 1, "Duplicate toolbar button creates a component");
            Click("UndoSceneButton");
            Check(session.Document.StructuralElements.Count == countBefore && Control<Button>("RedoSceneButton").IsEnabled,
                "Undo toolbar button reverses duplication and enables redo");
            Click("RedoSceneButton");
            Check(session.Document.StructuralElements.Count == countBefore + 1, "Redo toolbar button restores the component");
            source.SelectSceneItem(duplicatedId);
            Click("DeleteObjectButton");
            Check(session.Document.StructuralElements.Count == countBefore, "Delete toolbar button removes the selected duplicate");
            Click("UndoSceneButton");
            Click("UndoSceneButton");
            source.SelectSceneItem(objectBefore.Id);
            Click("RotateObjectButton");
            Check(session.Document.StructuralElements.Single(x => x.Id == objectBefore.Id).Rotation != objectBefore.Rotation,
                "Rotate toolbar button changes the selected component");
            Click("UndoSceneButton");
            source.SelectSceneItem(objectBefore.Id);
            Click("MoveGizmoButton");
            Pump();
            Check(Control<Canvas>("TransformGizmo").Children.OfType<System.Windows.Controls.Primitives.Thumb>().Count() >= 2,
                "Selected component exposes projected, draggable world axes");
            var handles = Control<Canvas>("TransformGizmo").Children.OfType<System.Windows.Controls.Primitives.Thumb>().ToArray();
            var handle = handles.First();
            handle.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
            handle.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(25, 10) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            handle.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(25, 10, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            Check(session.Document.StructuralElements.Single(x => x.Id == objectBefore.Id).Start != objectBefore.Start,
                "Axis drag commits a real object move");
            session.Undo();
            Check(session.Document.StructuralElements.Single(x => x.Id == objectBefore.Id).Start == objectBefore.Start,
                "One undo restores the complete axis drag");
            source.MoveSelection(0, 0, 0.5);
            Check(Math.Abs(session.Document.StructuralElements.Single(x => x.Id == objectBefore.Id).Offset - objectBefore.Offset - 0.5) < 1e-6,
                "Vertical axis elevates components in meters");
            session.Undo();
            Click("MoveGizmoButton");
            search.Text = "";
            var categoryFilter = Control<ComboBox>("SceneCategoryFilter");
            categoryFilter.SelectedItem = "Muros";
            Check(Control<StackPanel>("SceneItemsPanel").Children.OfType<Expander>().All(x => x.Header.ToString()!.Contains("Muros")),
                "Scene category filter narrows the actual object hierarchy");
            search.Text = "Suelo";
            Check(Control<StackPanel>("SceneItemsPanel").Children.Count == 0, "Category and text filters compose");
            search.Text = "";
            categoryFilter.SelectedIndex = 0;
            Pump();
            SaveBitmap("artifacts/studio-checks/03-inspector.png");
            Click("SceneInspectorButton");
            Call("SkyLibrary_Click", panel, new RoutedEventArgs());
            Pump();
            var skies = Control<System.Windows.Controls.Primitives.UniformGrid>("SkyTiles");
            Check(skies.Children.Count > 5, "Sky library exposes engine presets as visual tiles");
            Control<TextBox>("SkySearch").Text = "missing-sky-9471";
            Check(skies.Children.OfType<Button>().All(x => x.Visibility == Visibility.Collapsed) && Control<TextBlock>("SkyEmptyHint").IsVisible,
                "Sky search reports an empty result without changing the scene");
            Control<TextBox>("SkySearch").Text = "";
            SaveBitmap("artifacts/studio-checks/04-skies.png");
            var tile = skies.Children.OfType<Button>().First();
            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check((Control<ComboBox>("SkyPicker").SelectedItem as ComboBoxItem)?.Tag?.ToString() == tile.Tag.ToString() &&
                  !Control<Border>("SkyLibrary").IsVisible, "Sky tile applies actual lighting settings and returns to the viewport");
            Click("RenderRailMaterials");
            Pump();
            Check(Control<WrapPanel>("GalleryTiles").Children.Count > 0, "Material library is populated");
            var material = Control<WrapPanel>("GalleryTiles").Children.OfType<Button>().First();
            material.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(source.RenderMaterials.Any(x => x.TexturePath == material.Tag.ToString()), "Material gallery changes the document assignment");
            Click("MaterialSettingsTab");
            Check(Control<StackPanel>("MaterialSettingsHost").Visibility == Visibility.Visible && Control<Expander>("GalleryExpander").Visibility == Visibility.Collapsed,
                "Material settings are accessible without scrolling through the library");
            Click("MaterialLibraryTab");
            Call("OpenMaterialPicker");
            Pump();
            var picker = Application.Current.Windows.OfType<MaterialPickerWindow>().Single();
            var pickerTiles = (WrapPanel)typeof(MaterialPickerWindow).GetField("_tiles", PrivateInstance)!.GetValue(picker)!;
            Check(picker.ActualWidth < 500 && pickerTiles.Children.Count is > 0 and <= 24,
                "Reference-ordered material palette limits each page to 24 thumbnails");
            var pickerSearch = (TextBox)typeof(MaterialPickerWindow).GetField("_search", PrivateInstance)!.GetValue(picker)!;
            pickerSearch.Text = "material-inexistente-9471";
            Check(pickerTiles.Children.Count == 0, "Material search filters the complete catalog");
            pickerSearch.Text = "";
            Pump();
            var pickerBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)picker.ActualWidth, (int)picker.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            pickerBitmap.Render(picker);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pickerBitmap));
            using (var stream = File.Create("artifacts/studio-checks/08-material-palette.png")) encoder.Save(stream);
            picker.Close();
            Pump();
            SaveBitmap("artifacts/studio-checks/05-materials.png");
            var photoCount = source.RenderScenes.Count;
            Click("NewPhotoButton");
            Pump();
            Check(source.RenderScenes.Count == photoCount + 1 && source.RenderScenes.Last().Thumbnail is { Length: > 0 },
                "Photo button stores a camera and a real viewport thumbnail");
            ProjectFile.Save("artifacts/studio-checks/studio.g3d", session.Document);
            var reloaded = ProjectFile.Load("artifacts/studio-checks/studio.g3d");
            Check(reloaded.RenderScenes.Count == source.RenderScenes.Count && reloaded.RenderMaterials.Count == source.RenderMaterials.Count,
                "Camera and material edits survive project round trip");
            _window.Width = 1100; _window.Height = 760;
            Pump();
            Check(Control<Button>("StudioSaveButton").IsVisible && panel.Surface.ActualWidth > 500 && panel.Surface.ActualHeight > 400,
                "Small desktop keeps project commands and a usable viewport");
            SaveBitmap("artifacts/studio-checks/06-compact.png");
            var toolItems = Control<System.Windows.Controls.Primitives.UniformGrid>("SceneToolItems");
            foreach (var group in new[] { "CreateToolsTab", "EditToolsTab", "ViewToolsTab" })
            {
                Click(group);
                Pump();
                var visible = toolItems.Children.OfType<Button>().Where(button => button.IsVisible).ToArray();
                Check(visible.Length == 6 && visible.All(button =>
                {
                    var position = button.TranslatePoint(new Point(0, 0), toolItems);
                    return position.X >= 0 && position.X + button.ActualWidth <= toolItems.ActualWidth + 1 &&
                        position.Y + button.ActualHeight <= toolItems.ActualHeight + 1;
                }), group + " exposes six fully accessible tools at compact size");
            }
            Check(Control<Border>("SceneToolsBar").ActualHeight < 125, "Grouped tools leave more vertical space for the scene");
            SaveBitmap("artifacts/studio-checks/06-compact.png");
            Click("StudioMovieModeButton");
            Pump();
            var timeline = (Canvas)typeof(RenderPanel).GetField("_timeline", PrivateInstance)!.GetValue(panel)!;
            Check(timeline.ActualWidth > 150, "Compact movie workspace retains an editable timeline");
            SaveBitmap("artifacts/studio-checks/09-movie-compact.png");
            Click("StudioPanoramaModeButton");
            Pump();
            SaveBitmap("artifacts/studio-checks/10-panorama-compact.png");
            Click("StudioPhotoModeButton");
            var context = SynchronizationContext.Current;
            // Release temporary save/reload snapshots before the memory-budgeted render on small machines.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(_window.Dispatcher));
            try
            {
                var output = Path.GetFullPath("artifacts/studio-checks/rendered");
                var existing = Directory.Exists(output) ? Directory.GetFiles(output, "*.png").Length : 0;
                var job = (Task)Call("RenderPhotosAsync", new PhotoRenderOptions([], "640x360", "Draft", false, output, false))!;
                Check(!Control<StackPanel>("ProjectCommands").IsEnabled && !Control<Button>("StudioMovieModeButton").IsEnabled &&
                      !Control<Button>("DuplicateObjectButton").IsEnabled, "Rendering disables project replacement, mode changes and object edits");
                var renderDeadline = DateTime.UtcNow.AddMinutes(5);
                while (!job.IsCompleted && DateTime.UtcNow < renderDeadline) Pump();
                Check(job.IsCompletedSuccessfully && Directory.Exists(output) && Directory.GetFiles(output, "*.png").Length > existing,
                    "Photo workflow renders and exports an actual PNG · " + Control<TextBlock>("StatusText").Text);
                var png = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Directory.GetFiles(output, "*.png").Last()));
                Check(png.PixelWidth >= 320 && png.PixelHeight >= 180, "Exported render can be decoded at usable resolution");
                Check(Control<StackPanel>("ProjectCommands").IsEnabled && Control<Button>("StudioMovieModeButton").IsEnabled,
                    "Render completion restores workspace commands");
                SaveBitmap("artifacts/studio-checks/07-rendered.png");
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        }
        catch
        {
            SaveBitmap("artifacts/studio-checks/failure.png");
            throw;
        }
        finally
        {
            SetField("_dirty", false);
            _window.Close();
        }
    }
}
