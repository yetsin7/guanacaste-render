using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Layer animation in the Movie bar, after Lumion's layer effect: a level of the building appears or disappears at a time
/// of the movie. Keys are small diamonds on the timeline (green appears, orange hides) that can be dragged or removed; the
/// playback preview and the rendered video show each level only while it is there.
/// </summary>
public partial class RenderPanel
{
    private static readonly Color LayerShown = Color.FromRgb(0x4C, 0xC3, 0x7A);
    private static readonly Color LayerHidden = Color.FromRgb(0xE8, 0x8A, 0x3A);
    private readonly LayerAnimation _layers = new();
    private LayerKey? _dragLayer;
    private string _layerState = "";
    private int _layerRequest;

    /// <summary>Whether the movie uses the layer keys: the effect is switched on and there is at least one key.</summary>
    private bool LayersActive => LayersAnimationCheck.IsChecked == true && !_layers.IsEmpty;

    private string LevelName(Guid id) =>
        _source?.Levels.FirstOrDefault(level => level.Id == id)?.Name ?? "Nivel";

    /// <summary>Opens the menu of levels under the Layers button of the Movie bar.</summary>
    private void ShowLayersMenu(Button anchor)
    {
        if (_source is null) return;
        _layers.KeepLevels(_source.Levels.Select(level => level.Id).ToList());
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        var now = (float)_playTime;
        menu.Items.Add(new MenuItem { Header = $"Tiempo actual: {Clock(now)}", IsEnabled = false });
        menu.Items.Add(new Separator());
        foreach (var level in _source.Levels.OrderBy(item => item.Elevation))
        {
            var id = level.Id;
            var entry = new MenuItem { Header = level.Name };
            entry.Items.Add(LayerItem("Aparece aquí", () => SetLayerKey(id, now, true)));
            entry.Items.Add(LayerItem("Se oculta aquí", () => SetLayerKey(id, now, false)));
            if (_layers.Keys.Any(key => key.LevelId == id))
            {
                entry.Items.Add(new Separator());
                entry.Items.Add(LayerItem("Quitar sus claves", () => { _layers.RemoveLevel(id); LayersChanged(); }));
            }
            menu.Items.Add(entry);
        }
        menu.Items.Add(new Separator());
        var clear = LayerItem("Quitar toda la animación de capas", () => { _layers.Clear(); LayersChanged(); });
        clear.IsEnabled = !_layers.IsEmpty;
        menu.Items.Add(clear);
        menu.IsOpen = true;
    }

    private static MenuItem LayerItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Sets a key and makes sure the effect is on, so the person sees it act right away.</summary>
    private void SetLayerKey(Guid levelId, float seconds, bool visible)
    {
        _layers.Set(levelId, seconds, visible);
        if (LayersAnimationCheck.IsChecked != true) LayersAnimationCheck.IsChecked = true;
        _fxActive.Add("layers");
        RefreshFxStack();
        LayersChanged();
        StatusText.Text = $"«{LevelName(levelId)}» {(visible ? "aparece" : "se oculta")} a los {Clock(seconds)}. Arrastra el rombo para cambiar el momento.";
    }

    private void Layers_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        LayersChanged();
    }

    private void LayersChanged()
    {
        ScheduleMovieSave();
        RedrawTimeline();
        ApplyLayerPreview(_playTime);
    }

    /// <summary>Seconds of the last layer key, so the timeline always has room to show it.</summary>
    private double LastLayerSecond() => _layers.IsEmpty ? 0 : _layers.Keys.Max(key => key.Seconds);

    /// <summary>Draws one diamond per key on the ruler row of the timeline.</summary>
    private void DrawLayerKeys()
    {
        if (_timeline is null || _layers.IsEmpty) return;
        foreach (var key in _layers.Keys)
        {
            var color = key.Visible ? LayerShown : LayerHidden;
            var diamond = new Border
            {
                Width = 12, Height = 12, Background = Frozen(color), BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
                RenderTransform = new RotateTransform(45, 6, 6), Cursor = Cursors.SizeWE, Tag = key,
                ToolTip = $"{LevelName(key.LevelId)} {(key.Visible ? "aparece" : "se oculta")} · {Clock(key.Seconds)}\n" +
                          "Arrastra: cambiar el momento · Clic derecho: más"
            };
            var captured = key;
            diamond.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                StopTour();
                _dragLayer = captured;
                _timeline.CaptureMouse();
            };
            diamond.ContextMenu = BuildLayerKeyMenu(captured);
            Canvas.SetLeft(diamond, TimeToX(key.Seconds) - 6);
            Canvas.SetTop(diamond, 18);
            _timeline.Children.Add(diamond);
        }
    }

    private ContextMenu BuildLayerKeyMenu(LayerKey key)
    {
        var menu = new ContextMenu();
        menu.Items.Add(LayerItem(key.Visible ? "Cambiar a «se oculta»" : "Cambiar a «aparece»",
            () => { _layers.Set(key.LevelId, key.Seconds, !key.Visible); LayersChanged(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(LayerItem("Quitar esta clave", () => { _layers.Remove(key); LayersChanged(); }));
        return menu;
    }

    /// <summary>Moves a dragged key to the time under the mouse; returns false when no key is being dragged.</summary>
    private bool DragLayerKey(double seconds)
    {
        if (_dragLayer is not { } key) return false;
        var moved = key with { Seconds = (float)Math.Round(seconds * 10) / 10f };
        _layers.Remove(key);
        _layers.Set(moved.LevelId, moved.Seconds, moved.Visible);
        _dragLayer = moved;
        RedrawTimeline();
        return true;
    }

    private bool EndLayerDrag()
    {
        if (_dragLayer is null) return false;
        _dragLayer = null;
        ScheduleMovieSave();
        ApplyLayerPreview(_playTime);
        return true;
    }

    /// <summary>
    /// Shows in the preview the levels that exist at a time of the movie. Scenes are built only when the set of hidden
    /// levels changes, and only the newest request is applied, so scrubbing never queues a pile of builds.
    /// </summary>
    private async void ApplyLayerPreview(double seconds)
    {
        if (_source is null) return;
        var hidden = LayersActive ? _layers.HiddenAt((float)seconds) : [];
        var key = LayerAnimation.StateKey(hidden);
        if (key == _layerState) return;
        _layerState = key;
        var request = ++_layerRequest;
        RenderScene? scene;
        try { scene = await _source.GetSceneAsync(hidden); }
        catch (Exception)
        {
            // The preview of a look is optional: a failed build must never close the program; the next change retries.
            _layerState = "";
            return;
        }
        // Resumes on the dispatcher in the program; hop back to it when a host continues elsewhere.
        void Show()
        {
            if (request != _layerRequest || scene is null || _showingFinal || _rendering || PreviewToggle.IsChecked != true) return;
            _preview?.SetScene(scene);
        }
        if (Dispatcher.CheckAccess()) Show();
        else if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(Show);
    }

    /// <summary>The source of per-look scenes for rendering the video, or null when the movie has no layer animation.</summary>
    private LayerSource? LayersForRender()
    {
        if (!LayersActive || _source is null) return null;
        var source = _source;
        _layers.KeepLevels(source.Levels.Select(level => level.Id).ToList());
        if (_layers.IsEmpty) return null;
        // The video renders on a worker thread; the window copies its document on the UI thread, then builds off it.
        var snapshot = new LayerAnimation();
        foreach (var key in _layers.Keys) snapshot.Set(key.LevelId, key.Seconds, key.Visible);
        return new LayerSource(snapshot, hidden => Dispatcher.Invoke(() => source.GetSceneAsync(hidden)).GetAwaiter().GetResult());
    }
}
