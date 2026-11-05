using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>The three things the Render workspace produces, as in Lumion's Photo, Movie and Panorama modes.</summary>
internal enum RenderMode
{
    Photo,
    Movie,
    Panorama
}

/// <summary>
/// The mode switch above the bottom bar, the movie timeline (camera marks along a time ruler, with play and scrubbing) and
/// the panorama bar. Photo mode keeps the strip of saved cameras; Movie mode edits the camera path; Panorama renders the
/// view from the camera position in every direction.
/// </summary>
public partial class RenderPanel
{
    private const double TimelinePadding = 28;
    private RenderMode _mode = RenderMode.Photo;
    private readonly List<(RenderMode Mode, Button Button)> _modeButtons = [];
    private Border? _movieStrip;
    private Border? _panoramaStrip;
    private Canvas? _timeline;
    private TextBlock? _timeText;
    private TextBlock? _moviePlayText;
    private Rectangle? _playhead;
    private int _dragMark = -1;

    /// <summary>True while the final image is a 360° panorama.</summary>
    private bool PanoramaMode => _mode == RenderMode.Panorama;

    private void InitializeModes()
    {
        ModeOverlayHost.Children.Clear();
        _modeButtons.Clear();
        OutputModeHost.Content = BuildModeSwitch();
        _movieStrip = BuildMovieStrip();
        _panoramaStrip = BuildPanoramaStrip();
        ModeOverlayHost.Children.Add(_movieStrip);
        ModeOverlayHost.Children.Add(_panoramaStrip);
        SetMode(RenderMode.Photo);
    }

    // ---------------------------------------------------------------- mode switch

    private FrameworkElement BuildModeSwitch()
    {
        var row = new WrapPanel();
        foreach (var (mode, label, glyph) in new[]
                 {
                     (RenderMode.Photo, "Foto", ""), (RenderMode.Movie, "Película", ""), (RenderMode.Panorama, "Panorama 360°", "")
                 })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = content, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(2), BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, FontSize = 12.5, ToolTip = mode switch
                {
                    RenderMode.Photo => "Fotos: cámaras guardadas con su propio estilo y filtros",
                    RenderMode.Movie => "Película: recorrido de cámara en una línea de tiempo",
                    _ => "Panorama: imagen esférica completa desde la posición de la cámara"
                }
            };
            var chosen = mode;
            button.Click += (_, _) => SetMode(chosen);
            _modeButtons.Add((mode, button));
            row.Children.Add(button);
        }
        return new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4), CornerRadius = new CornerRadius(9), Padding = new Thickness(2),
            Background = Frozen(Color.FromArgb(0xEC, 0x0F, 0x1B, 0x26)), BorderBrush = CardBorderIdle, BorderThickness = new Thickness(1),
            Child = row
        };
    }

    private void SetMode(RenderMode mode)
    {
        if (_rendering || _batchRunning || _renderingTour) mode = _mode;
        var panoramaChanged = (mode == RenderMode.Panorama) != PanoramaMode;
        _mode = mode;
        PhotoStrip.Visibility = mode == RenderMode.Photo ? Visibility.Visible : Visibility.Collapsed;
        if (_movieStrip is not null) _movieStrip.Visibility = mode == RenderMode.Movie ? Visibility.Visible : Visibility.Collapsed;
        if (_panoramaStrip is not null) _panoramaStrip.Visibility = mode == RenderMode.Panorama ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (buttonMode, button) in _modeButtons)
        {
            var active = buttonMode == mode;
            button.Background = active ? RailActive : Brushes.Transparent;
            button.Foreground = active ? RailActiveText : RailInactiveText;
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
        if (mode != RenderMode.Movie) StopTour();
        if (mode == RenderMode.Movie) RedrawTimeline();
        if (panoramaChanged && _ready && _source is not null) OnCameraChanged();
    }

    // ---------------------------------------------------------------- panorama bar

    private Border BuildPanoramaStrip()
    {
        var button = PrimaryBarButton("", "Renderizar panorama");
        button.Click += (_, e) => RenderButton_Click(button, e);
        var grid = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = "Panorama 360°: se renderiza todo lo que rodea a la cámara en una imagen 2:1 (equirrectangular) de 4096 × 2048 píxeles " +
                   "según la calidad elegida. Colócate donde quieras estar y pulsa Renderizar. Útil para visores de realidad virtual.",
            Foreground = Frozen(Color.FromRgb(0xC9, 0xD8, 0xE4)), FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0)
        });
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return new Border
        {
            Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 41),
            Height = 124, Background = Frozen(Color.FromArgb(0xF2, 0x0F, 0x1B, 0x26)), BorderBrush = Frozen(Color.FromRgb(0x38, 0x50, 0x5A)),
            BorderThickness = new Thickness(0, 1, 0, 0), Child = grid
        };
    }

    private Button PrimaryBarButton(string glyph, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return new Button { Content = content, Style = (Style)FindResource("RenderPrimaryButton"), MinHeight = 44, MinWidth = 190 };
    }

    // ---------------------------------------------------------------- movie bar and timeline

    private Border BuildMovieStrip()
    {
        Button Small(string glyph, string text, string tip, RoutedEventHandler click, out TextBlock label)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(label);
            var button = new Button { Content = content, ToolTip = tip, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6), MinHeight = 30 };
            button.Click += click;
            return button;
        }

        var play = Small("", "Reproducir", "Reproduce el recorrido de la cámara", (_, e) => PlayTour_Click(this, e), out var playLabel);
        _moviePlayText = playLabel;
        var add = Small("", "Añadir cámara", "Agrega la vista actual como una marca al final del recorrido", AddMark_Click, out _);
        var fromPhotos = Small("", "Desde las fotos", "Crea el recorrido con las cámaras de las fotos, en orden", (_, _) => MarksFromPhotos(), out _);
        var clear = Small("", "Vaciar", "Quita todas las marcas", (_, _) => { _path.Clear(); RefreshMarks(); }, out _);
        Button? layersButton = null;
        layersButton = Small("", "Capas", "Hace aparecer o desaparecer niveles del edificio en un momento de la película",
            (_, _) => ShowLayersMenu(layersButton!), out _);
        var left = new WrapPanel { Width = 420, Margin = new Thickness(14, 12, 6, 0) };
        left.Children.Add(play);
        left.Children.Add(add);
        left.Children.Add(fromPhotos);
        left.Children.Add(clear);
        left.Children.Add(layersButton);
        left.Children.Add(Small("", "Personas y coches", "Coloca personas que caminan y coches que circulan por rutas: se ven en el render y en la película",
            (_, _) => ShowPeopleWindow(), out _));
        left.Children.Add(Small("", "Árboles y rocas", "Esparce árboles, plantas y rocas en un círculo alrededor del punto que mira la vista",
            (_, _) => ShowScatterWindow(), out _));

        _timeText = new TextBlock { Foreground = Frozen(Color.FromRgb(0x8F, 0xA6, 0xB9)), FontSize = 11.5, Margin = new Thickness(0, 10, 0, 4) };
        _timeline = new Canvas { Height = 74, Background = Brushes.Transparent, ClipToBounds = true };
        _timeline.MouseLeftButtonDown += Timeline_MouseDown;
        _timeline.MouseMove += Timeline_MouseMove;
        _timeline.MouseLeftButtonUp += Timeline_MouseUp;
        _timeline.SizeChanged += (_, _) => RedrawTimeline();
        var center = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
        center.Children.Add(_timeText);
        center.Children.Add(new Border
        {
            Background = Frozen(Color.FromRgb(0x0E, 0x18, 0x22)), BorderBrush = CardBorderIdle, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Child = _timeline
        });

        var render = PrimaryBarButton("", "Renderizar película");
        render.MinWidth = 150;
        render.Click += (_, e) => RenderTour_Click(render, e);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 16, 0), Width = 160 };
        right.Children.Add(render);
        right.Children.Add(new TextBlock
        {
            Text = "Se guarda una secuencia de imágenes; los cuadros por segundo se eligen en la pestaña Cámara.",
            Foreground = Frozen(Color.FromRgb(0x8F, 0xA6, 0xB9)), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(center, 1);
        grid.Children.Add(center);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return new Border
        {
            Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 41),
            Height = 124, Background = Frozen(Color.FromArgb(0xF2, 0x0F, 0x1B, 0x26)), BorderBrush = Frozen(Color.FromRgb(0x38, 0x50, 0x5A)),
            BorderThickness = new Thickness(0, 1, 0, 0), Child = grid
        };
    }

    private static string Clock(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);

    /// <summary>Seconds at which each mark is reached; the first mark is at zero.</summary>
    private double[] MarkTimes()
    {
        var times = new double[_path.Marks.Count];
        for (var i = 1; i < times.Length; i++) times[i] = times[i - 1] + Math.Max(0.05f, _path.Marks[i].SecondsFromPrevious);
        return times;
    }

    private double TimelineSpan() => Math.Max(10, Math.Max(_path.Duration, LastLayerSecond()) + 2);

    private double TimeToX(double seconds) =>
        TimelinePadding + seconds / TimelineSpan() * Math.Max(1, (_timeline?.ActualWidth ?? 0) - 2 * TimelinePadding);

    private double XToTime(double x) =>
        Math.Clamp((x - TimelinePadding) / Math.Max(1, (_timeline?.ActualWidth ?? 0) - 2 * TimelinePadding) * TimelineSpan(), 0, TimelineSpan());

    /// <summary>Draws the ruler, the camera marks and the playhead.</summary>
    internal void RedrawTimeline()
    {
        if (_timeline is null || _timeline.ActualWidth < 10) return;
        _timeline.Children.Clear();
        var line = Frozen(Color.FromRgb(0x4A, 0x68, 0x70));
        var text = Frozen(Color.FromRgb(0x8F, 0xA6, 0xB9));
        var span = TimelineSpan();
        var step = span > 90 ? 10 : span > 40 ? 5 : span > 16 ? 2 : 1;
        for (var second = 0; second <= span; second += step)
        {
            var x = TimeToX(second);
            _timeline.Children.Add(new Rectangle { Width = 1, Height = 8, Fill = line, Margin = new Thickness(x, 4, 0, 0) });
            var label = new TextBlock { Text = $"{second}s", Foreground = text, FontSize = 9.5 };
            Canvas.SetLeft(label, x + 3);
            Canvas.SetTop(label, 2);
            _timeline.Children.Add(label);
        }
        var track = new Rectangle { Height = 3, Width = Math.Max(1, _timeline.ActualWidth - 2 * TimelinePadding), Fill = line };
        Canvas.SetLeft(track, TimelinePadding);
        Canvas.SetTop(track, 44);
        _timeline.Children.Add(track);

        var times = MarkTimes();
        var selected = MarksList.SelectedIndex;
        if (times.Length > 1)
        {
            var filled = new Rectangle { Height = 3, Width = Math.Max(1, TimeToX(times[^1]) - TimelinePadding), Fill = CardBorderActive };
            Canvas.SetLeft(filled, TimelinePadding);
            Canvas.SetTop(filled, 44);
            _timeline.Children.Add(filled);
        }
        for (var i = 0; i < times.Length; i++)
        {
            var active = i == selected;
            var chip = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Cursor = i == 0 ? Cursors.Arrow : Cursors.SizeWE, Tag = i,
                Background = active ? CardBorderActive : Frozen(Color.FromRgb(0x25, 0x40, 0x4F)), BorderBrush = CardBorderActive,
                BorderThickness = new Thickness(2),
                ToolTip = $"Cámara {i + 1} · {Clock(times[i])}\nClic: ir a esta cámara · Arrastra: cambiar el tiempo · Clic derecho: más",
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(CultureInfo.InvariantCulture), FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = active ? Brushes.Black : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var index = i;
            chip.MouseLeftButtonDown += (_, e) => Chip_MouseDown(index, chip, e);
            chip.ContextMenu = BuildMarkMenu(i);
            Canvas.SetLeft(chip, TimeToX(times[i]) - 13);
            Canvas.SetTop(chip, 32);
            _timeline.Children.Add(chip);
        }
        DrawLayerKeys();
        _playhead = new Rectangle { Width = 2, Height = 70, Fill = Frozen(Color.FromRgb(0xFF, 0xB3, 0x47)), IsHitTestVisible = false };
        Canvas.SetTop(_playhead, 2);
        Canvas.SetLeft(_playhead, TimeToX(_playTime));
        _timeline.Children.Add(_playhead);
        if (_timeText is not null)
            _timeText.Text = times.Length < 2
                ? "Añade al menos dos cámaras para crear el recorrido."
                : $"{Clock(_playTime)} / {Clock(_path.Duration)} · {times.Length} cámaras";
    }

    private double _playTime;

    /// <summary>Moves the playhead without redrawing the whole timeline.</summary>
    private void UpdatePlayhead(double seconds)
    {
        _playTime = Math.Clamp(seconds, 0, TimelineSpan());
        if (_playhead is not null) Canvas.SetLeft(_playhead, TimeToX(_playTime));
        ApplyLayerPreview(_playTime);
        ApplyMotionPreview();
        if (_timeText is not null && _path.Marks.Count >= 2) _timeText.Text = $"{Clock(_playTime)} / {Clock(_path.Duration)} · {_path.Marks.Count} cámaras";
    }

    private void SyncPlayButton(bool playing)
    {
        if (_moviePlayText is not null) _moviePlayText.Text = playing ? "Detener" : "Reproducir";
    }

    private void Chip_MouseDown(int index, UIElement chip, MouseButtonEventArgs e)
    {
        e.Handled = true;
        StopTour();
        MarksList.SelectedIndex = index;
        var mark = _path.Marks[index];
        ApplyCamera(mark.Position, mark.Target, mark.FieldOfViewDegrees);
        _playTime = MarkTimes()[index];
        ApplyLayerPreview(_playTime);
        ApplyMotionPreview();
        _dragMark = index > 0 ? index : -1;
        RedrawTimeline();
        if (_dragMark >= 0 && _timeline is not null) _timeline.CaptureMouse();
    }

    private void Timeline_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // The empty ruler scrubs the camera along the path.
        if (_path.Marks.Count < 2) return;
        StopTour();
        ScrubTo(XToTime(e.GetPosition(_timeline).X));
        _scrubbing = true;
        _timeline?.CaptureMouse();
    }

    private bool _scrubbing;

    private void ScrubTo(double seconds)
    {
        seconds = Math.Min(seconds, _path.Duration);
        var (position, target, fieldOfView) = _path.Sample((float)seconds);
        ApplyCamera(position, target, fieldOfView);
        UpdatePlayhead(seconds);
    }

    private void Timeline_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _timeline is null) return;
        var time = XToTime(e.GetPosition(_timeline).X);
        if (DragLayerKey(time)) return;
        if (_scrubbing && _path.Marks.Count >= 2)
        {
            ScrubTo(time);
            return;
        }
        if (_dragMark < 1 || _dragMark >= _path.Marks.Count) return;
        var times = MarkTimes();
        var seconds = (float)Math.Max(0.2, time - times[_dragMark - 1]);
        _path.Replace(_dragMark, _path.Marks[_dragMark] with { SecondsFromPrevious = seconds });
        RedrawTimeline();
    }

    private void Timeline_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _timeline?.ReleaseMouseCapture();
        if (EndLayerDrag()) return;
        var moved = _dragMark >= 0;
        _dragMark = -1;
        _scrubbing = false;
        if (moved) RefreshMarks();
    }

    private ContextMenu BuildMarkMenu(int index)
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }
        menu.Items.Add(Item("Ir a esta cámara", () =>
        {
            var mark = _path.Marks[index];
            ApplyCamera(mark.Position, mark.Target, mark.FieldOfViewDegrees);
        }));
        menu.Items.Add(Item("Actualizar con la vista actual", () =>
        {
            if (_source is null) return;
            var camera = _source.Camera;
            var focus = _source.Query?.Pick(camera.Position, camera.Forward) is { } distance ? Math.Max(1f, distance) : 10f;
            _path.Replace(index, _path.Marks[index] with
            {
                Position = camera.Position, Target = camera.Position + camera.Forward * focus, FieldOfViewDegrees = camera.FieldOfViewDegrees
            });
            RefreshMarks();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Eliminar", () =>
        {
            _path.RemoveAt(index);
            RefreshMarks();
        }));
        return menu;
    }

    /// <summary>Builds the path from the saved photo cameras, in order, four seconds apart.</summary>
    private void MarksFromPhotos()
    {
        if (_source is null) return;
        var photos = _source.RenderScenes;
        if (photos.Count == 0)
        {
            StatusText.Text = "Guarda al menos una foto para crear el recorrido con sus cámaras.";
            return;
        }
        _path.Clear();
        foreach (var photo in photos)
        {
            var setup = photo.Setup;
            var camera = new NavigationCamera
            {
                Position = new Vector3((float)setup.CameraX, (float)setup.CameraY, (float)setup.CameraZ),
                YawDegrees = (float)setup.YawDegrees, PitchDegrees = (float)setup.PitchDegrees
            };
            _path.Add(new CameraMark(camera.Position, camera.Position + camera.Forward * 10f, (float)setup.FieldOfViewDegrees, 4, photo.Name));
        }
        RefreshMarks();
        StatusText.Text = $"Recorrido creado con {photos.Count} cámaras. Arrastra las marcas para ajustar los tiempos.";
    }
}
