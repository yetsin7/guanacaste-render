using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private bool _skyLibraryBuilt;
    private string _skyCategory = "all";
    private readonly SemaphoreSlim _skyPreviewGate = new(2, 2);
    private bool _updatingSceneCategories;
    private readonly HashSet<string> _collapsedSceneGroups = [];
    private readonly Dictionary<Guid, Button> _sceneRowButtons = [];
    private string _toolGroup = "create";
    private bool _showNavigationHelp;

    private void NavigationHelp_Click(object sender, RoutedEventArgs e)
    {
        _showNavigationHelp = !_showNavigationHelp;
        InteractiveHintCard.Visibility = _showNavigationHelp && !_showingFinal ? Visibility.Visible : Visibility.Collapsed;
        NavigationHelpButton.Background = _showNavigationHelp ? RailActive : RailInactive;
    }

    private void ToolGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string group }) SelectToolGroup(group);
    }

    private void SelectToolGroup(string group)
    {
        _toolGroup = group;
        foreach (var button in SceneToolItems.Children.OfType<Button>())
            button.Visibility = button.CommandParameter as string == group ? Visibility.Visible : Visibility.Collapsed;
        foreach (var tab in new[] { CreateToolsTab, EditToolsTab, ViewToolsTab })
            tab.Background = tab.Tag as string == group ? RailActive : Brushes.Transparent;
    }
    private bool StudioBusy => _renderStarting || _rendering || _renderingTour || _batchRunning;

    private void RefreshStudioActions()
    {
        if (_source is null || SceneToolItems is null) return;
        var busy = StudioBusy;
        ProjectCommands.IsEnabled = SectionTabs.IsEnabled = SceneToolItems.IsEnabled = !busy;
        SceneInspector.IsEnabled = !busy;
        ModeOverlayHost.IsEnabled = PhotoStrip.IsEnabled = !busy;
        CreateToolsTab.IsEnabled = EditToolsTab.IsEnabled = ViewToolsTab.IsEnabled = !busy;
        EyeHeightControls.IsEnabled = !busy && _source.HasScene;
        TransformGizmo.IsHitTestVisible = !busy;
        foreach (var window in new Window?[] { _materialPicker, _scatterWindow, _peopleWindow, _fireWindow })
            if (window is { IsLoaded: true }) window.IsEnabled = !busy;
        foreach (var button in SceneToolItems.Children.OfType<Button>())
        {
            if (button.Tag is string command) button.IsEnabled = _source.CanExecuteSceneCommand(command);
            ToolTipService.SetShowOnDisabled(button, true);
        }
        MoveGizmoButton.IsEnabled = _source.CanMoveSelection;
        NewPhotoButton.IsEnabled = !busy && _source.HasScene;
        RenderPhotosButton.IsEnabled = !busy && _source.HasScene;
        foreach (var button in new[] { StudioBuildModeButton, StudioPhotoModeButton, StudioMovieModeButton, StudioPanoramaModeButton })
            button.IsEnabled = !busy;
        foreach (var (_, button) in _modeButtons) button.IsEnabled = !busy;
        var selected = _source.SelectedSceneItemId;
        var item = selected is null ? null : _source.SceneItems().FirstOrDefault(x => x.Id == selected);
        SceneSelectionText.Text = item is null ? "Selecciona un objeto para mover, girar, duplicar o eliminar" : $"{item.Name}  ·  {item.Layer}";
        foreach (var (id, button) in _sceneRowButtons)
            button.Background = id == selected ? RailActive : Brushes.Transparent;
        UpdateStudioOverlayLayout();
    }

    private void UpdateStudioOverlayLayout()
    {
        if (SceneInspector is null || SceneToolsBar is null) return;
        SceneInspector.Margin = new Thickness(18, 60, 0, SceneToolsBar.Margin.Bottom + SceneToolsBar.ActualHeight + 12);
    }

    private void StudioMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<RenderMode>(tag, out var mode)) SetMode(mode);
    }

    private void MaterialView_Click(object sender, RoutedEventArgs e)
    {
        var settings = sender is Button { Tag: "settings" };
        MaterialSettingsHost.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        GalleryExpander.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        MaterialSettingsTab.Background = settings ? RailActive : RailInactive;
        MaterialLibraryTab.Background = settings ? RailInactive : RailActive;
    }

    private void InitializeStudio()
    {
        SelectToolGroup(_toolGroup);
        SceneToolsBar.SizeChanged += (_, _) => UpdateStudioOverlayLayout();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (FxPicker.Visibility == Visibility.Visible)
            { CloseFxPicker(); e.Handled = true; }
            else if (SkyLibrary.Visibility == Visibility.Visible)
            { SkyLibrary.Visibility = Visibility.Collapsed; e.Handled = true; }
            else if (SceneInspector.Visibility == Visibility.Visible)
            { SceneInspector.Visibility = Visibility.Collapsed; SceneInspectorButton.Focus(); e.Handled = true; }
            else if (_settingsWidthBeforeTheater is not null)
            { Theater_Click(TheaterButton, new RoutedEventArgs()); e.Handled = true; }
            else if (_showNavigationHelp)
            { NavigationHelp_Click(NavigationHelpButton, new RoutedEventArgs()); e.Handled = true; }
        };
    }

    private void RefreshStudio()
    {
        if (_source is null) return;
        StudioProjectText.Text = _source.ProjectName;
        RefreshStudioActions();
        // Linked imports also have geometry, even when they do not expose native editable objects.
        StudioWelcome.Visibility = _source.HasScene ? Visibility.Collapsed : Visibility.Visible;
        if (SceneInspector.Visibility == Visibility.Visible) RefreshSceneInspector();
    }

    private void SceneInspector_Click(object sender, RoutedEventArgs e)
    {
        SceneInspector.Visibility = SceneInspector.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (SceneInspector.Visibility == Visibility.Visible)
        {
            RefreshSceneInspector();
            SceneSearch.Focus();
        }
    }

    private void SceneSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready && SceneInspector.Visibility == Visibility.Visible) RefreshSceneInspector();
    }

    private void SceneCategoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_updatingSceneCategories && SceneInspector.IsVisible) RefreshSceneInspector();
    }

    private void SkySearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (SkyTiles is null || SkyEmptyHint is null) return;
        var count = 0;
        foreach (var tile in SkyTiles.Children.OfType<Button>())
        {
            var sky = RenderLooks.Skies.First(x => x.Id == tile.Tag as string);
            var categoryMatches = _skyCategory switch
            {
                "day" => sky.SolarHour is > 7 and < 17,
                "sunset" => sky.SolarHour is >= 17 and <= 19 || sky.SolarHour is >= 5 and <= 7,
                "night" => sky.SolarHour > 19 || sky.SolarHour < 5,
                "clouds" => sky.CloudCover >= .5,
                _ => true
            };
            var matches = categoryMatches && MaterialCatalogSearch.Matches(SkySearch.Text.Trim(), tile.ToolTip?.ToString() ?? "");
            tile.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            if (matches) count++;
        }
        SkyEmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SkyCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category }) return;
        _skyCategory = category;
        foreach (var button in SkyCategories.Children.OfType<Button>())
            button.Background = button.Tag as string == category ? RailActive : Brushes.Transparent;
        SkySearch_Changed(SkySearch, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
    }

    private void RefreshSceneInspector(IReadOnlyList<RenderSceneItem>? items = null)
    {
        if (_source is null) return;
        items ??= _source.SceneItems();
        var categories = new[] { "Todas las categorías" }.Concat(items.Select(x => x.Category).Distinct().Order()).ToArray();
        if (!SceneCategoryFilter.Items.Cast<string>().SequenceEqual(categories))
        {
            var selected = SceneCategoryFilter.SelectedItem as string;
            _updatingSceneCategories = true;
            try
            {
                SceneCategoryFilter.ItemsSource = categories;
                SceneCategoryFilter.SelectedItem = categories.Contains(selected) ? selected : categories[0];
            }
            finally { _updatingSceneCategories = false; }
        }
        var query = SceneSearch.Text.Trim();
        var category = SceneCategoryFilter.SelectedIndex > 0 ? SceneCategoryFilter.SelectedItem as string : null;
        var matches = items.Where(x => (category is null || x.Category == category) &&
            MaterialCatalogSearch.Matches(query, x.Name, x.Category, x.Layer)).ToArray();
        SceneItemsPanel.Children.Clear();
        _sceneRowButtons.Clear();
        // Bound UI creation for large BIM imports; searching narrows the complete catalog before the cap.
        foreach (var group in matches.Take(250).GroupBy(x => (x.Layer, x.Category)))
        {
            var key = group.Key.Layer + "/" + group.Key.Category;
            var body = new StackPanel();
            foreach (var item in group)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var eye = new CheckBox
                {
                    IsChecked = item.Visible, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(3, 0, 8, 0), ToolTip = "Mostrar u ocultar · se guarda en el proyecto"
                };
                AutomationProperties.SetName(eye, "Visible: " + item.Name);
                eye.Click += (_, _) => { _source.SetSceneItemVisible(item.Id, eye.IsChecked == true); RefreshSceneInspector(); };
                DockPanel.SetDock(eye, Dock.Left);
                row.Children.Add(eye);
                var select = new Button
                {
                    Content = new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                    HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(7, 6, 7, 6),
                    Background = item.Id == _source.SelectedSceneItemId ? RailActive : Brushes.Transparent, BorderThickness = new Thickness(0),
                    ToolTip = item.Name + " · " + item.Layer, Tag = item.Id
                };
                AutomationProperties.SetName(select, "Seleccionar " + item.Name);
                _sceneRowButtons[item.Id] = select;
                select.Click += (_, _) =>
                {
                    _source.SelectSceneItem(item.Id);
                    StatusText.Text = $"Seleccionado: {item.Name} · {item.Layer}";
                };
                row.Children.Add(select);
                body.Children.Add(row);
            }
            var section = new Expander
            {
                Header = $"{group.Key.Layer} · {group.Key.Category} ({group.Count()})",
                IsExpanded = query.Length > 0 || !_collapsedSceneGroups.Contains(key),
                Content = body, Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 4)
            };
            section.Collapsed += (_, _) => _collapsedSceneGroups.Add(key);
            section.Expanded += (_, _) => _collapsedSceneGroups.Remove(key);
            SceneItemsPanel.Children.Add(section);
        }
        SceneSummary.Text = matches.Length == 0 ? "Sin resultados. Busca otro nombre o nivel."
            : $"{matches.Length} objetos nativos · {items.Count(x => x.Visible)} visibles" +
              (matches.Length > 250 ? " · Mostrando 250; busca para acotar." : "");
    }

    private void SkyLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (!_skyLibraryBuilt)
        {
            _skyLibraryBuilt = true;
            foreach (var sky in RenderLooks.Skies)
            {
                var picture = new Image { Height = 125, Stretch = Stretch.UniformToFill };
                var content = new StackPanel();
                content.Children.Add(picture);
                content.Children.Add(new TextBlock { Text = Text(sky.NameKey), Margin = new Thickness(6, 9, 6, 5),
                    Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
                var tile = new Button { Content = content, Margin = new Thickness(1), Padding = new Thickness(1),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = Text(sky.NameKey), Tag = sky.Id };
                AutomationProperties.SetName(tile, Text(sky.NameKey));
                tile.Click += (_, _) =>
                {
                    Select(SkyPicker, sky.Id);
                    SkyLibrary.Visibility = Visibility.Collapsed;
                    StatusText.Text = "Cielo aplicado: " + Text(sky.NameKey);
                };
                var loaded = false;
                picture.Loaded += async (_, _) =>
                {
                    if (loaded) return;
                    loaded = true;
                    await _skyPreviewGate.WaitAsync();
                    try { picture.Source = await Task.Run(() => RenderLookThumbnail.Sky(sky)); }
                    catch (InvalidOperationException) { loaded = false; }
                    finally { _skyPreviewGate.Release(); }
                };
                SkyTiles.Children.Add(tile);
            }
        }
        SkyLibrary.Visibility = Visibility.Visible;
        SkySearch_Changed(SkySearch, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
        SkySearch.Focus();
    }

    private void SkyLibraryClose_Click(object sender, RoutedEventArgs e) => SkyLibrary.Visibility = Visibility.Collapsed;
}
