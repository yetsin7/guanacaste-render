using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Lumion-style photos: each one keeps a camera together with its own style, filters and sky. Opening a photo flies the
/// camera there and loads its look; adjusting a filter updates the open photo automatically.
/// </summary>
public partial class RenderPanel
{
    private static readonly Brush CardIdle = Frozen(Color.FromRgb(0x1B, 0x29, 0x37));
    private static readonly Brush CardActive = Frozen(Color.FromRgb(0x1F, 0x55, 0x60));
    private static readonly Brush CardBorderIdle = Frozen(Color.FromRgb(0x34, 0x49, 0x5A));
    private static readonly Brush CardBorderActive = Frozen(Color.FromRgb(0x66, 0xD9, 0xC4));

    private Guid? _activePhotoId;
    private RenderSetup? _photoBaseline;
    private DispatcherTimer? _photoWatch;
    private DispatcherTimer? _flightTimer;
    private bool _applyingPhoto;

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Starts the watcher that stores filter changes in the open photo and the keyboard shortcuts.</summary>
    private void InitializePhotos()
    {
        PreviewKeyDown += PhotoKeyDown;
        _photoWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _photoWatch.Tick += (_, _) => WatchPhotoEdits();
        _photoWatch.Start();
    }

    // ---------------------------------------------------------------- strip

    /// <summary>Rebuilds the photo cards from the project, dropping the active mark if its photo disappeared.</summary>
    internal void RefreshPhotos()
    {
        if (_source is null || PhotoCards is null) return;
        var photos = _source.RenderScenes;
        if (_activePhotoId is { } active && photos.All(photo => photo.Id != active))
        {
            _activePhotoId = null;
            _photoBaseline = null;
        }
        PhotoCards.Children.Clear();
        for (var index = 0; index < photos.Count; index++) PhotoCards.Children.Add(BuildPhotoCard(photos[index], index + 1));
        // Lumion keeps ten camera slots per set: the free ones are shown so a click (or Ctrl+number) fills the next.
        for (var number = photos.Count + 1; number <= SlotCount; number++) PhotoCards.Children.Add(BuildEmptySlot(number, number == photos.Count + 1));
        PhotoEmptyHint.Visibility = Visibility.Collapsed;
        PhotoCountText.Text = photos.Count switch { 0 => "", 1 => "1 foto", _ => $"{photos.Count} fotos" };
    }

    private const int SlotCount = 10;

    /// <summary>A numbered, empty camera slot; only the next free one can be filled by a click, the others keep their order.</summary>
    private FrameworkElement BuildEmptySlot(int number, bool next)
    {
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        label.Children.Add(new TextBlock
        {
            Text = (number % 10).ToString(CultureInfo.InvariantCulture), FontSize = 22, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0x78, 0x8A))
        });
        label.Children.Add(new TextBlock
        {
            Text = next ? "Guardar la vista" : "Libre", FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(next ? (byte)0x8F : (byte)0x5E, next ? (byte)0xA6 : (byte)0x78, next ? (byte)0xB9 : (byte)0x8A))
        });
        var slot = new Border
        {
            Width = 138, Height = 106, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(8), Child = label,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderBrush = CardBorderIdle,
            BorderThickness = new Thickness(1), Cursor = next ? Cursors.Hand : Cursors.Arrow,
            ToolTip = next ? $"Guarda la vista actual en la cámara {number % 10} (Ctrl+{number % 10})" : $"Cámara {number % 10} libre",
            Tag = number
        };
        if (next) slot.MouseLeftButtonDown += (_, e) => { AddPhoto(); e.Handled = true; };
        return slot;
    }

    private FrameworkElement BuildPhotoCard(RenderSceneBookmark photo, int number)
    {
        var selected = photo.Id == _activePhotoId;
        var picture = new Image
        {
            Width = 128, Height = 72, Stretch = Stretch.UniformToFill, Source = PhotoThumbnails.Of(photo),
            Clip = new RectangleGeometry(new Rect(0, 0, 128, 72), 5, 5)
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 10, 20, 30)), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Child = new TextBlock { Text = number <= 10 ? (number % 10).ToString(CultureInfo.InvariantCulture) : number.ToString(CultureInfo.InvariantCulture),
                Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold }
        };
        var delete = new Button
        {
            Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(0, 4, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed, ToolTip = "Eliminar foto", FontSize = 11, Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromArgb(220, 120, 30, 40)), Foreground = Brushes.White, BorderThickness = new Thickness(0)
        };
        delete.Click += (_, e) => { e.Handled = true; DeletePhoto(photo.Id); };
        var thumb = new Grid { Width = 128, Height = 72, Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x18, 0x22)) };
        thumb.Children.Add(picture);
        thumb.Children.Add(badge);
        thumb.Children.Add(delete);
        var name = new TextBlock
        {
            Text = photo.Name, Foreground = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC6, 0xD6, 0xE3)),
            FontSize = 11.5, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(2, 5, 2, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var stack = new StackPanel();
        stack.Children.Add(thumb);
        stack.Children.Add(name);
        var card = new Border
        {
            Width = 138, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(4), CornerRadius = new CornerRadius(8),
            Background = selected ? CardActive : CardIdle, BorderBrush = selected ? CardBorderActive : CardBorderIdle,
            BorderThickness = new Thickness(selected ? 2 : 1), Cursor = Cursors.Hand, Child = stack,
            ToolTip = $"{photo.Name}\nClic: abrir · Doble clic: renombrar · Clic derecho: más opciones" +
                      (number <= 10 ? $"\nMayús+{number % 10}: abrir · Ctrl+{number % 10}: actualizar encuadre" : "")
        };
        card.MouseEnter += (_, _) => delete.Visibility = Visibility.Visible;
        card.MouseLeave += (_, _) => delete.Visibility = Visibility.Collapsed;
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) RenamePhoto(photo.Id);
            else ActivatePhoto(photo.Id, animate: true);
            e.Handled = true;
        };
        card.ContextMenu = BuildPhotoMenu(photo.Id);
        return card;
    }

    private ContextMenu BuildPhotoMenu(Guid id)
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }
        menu.Items.Add(Item("Abrir foto", () => ActivatePhoto(id, animate: true)));
        menu.Items.Add(Item("Actualizar encuadre con la cámara actual", () => UpdatePhotoView(id)));
        menu.Items.Add(Item("Renombrar…", () => RenamePhoto(id)));
        menu.Items.Add(Item("Duplicar", () => DuplicatePhoto(id)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Renderizar esta foto…", () => OpenRenderPhotosDialog(id)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Eliminar", () => DeletePhoto(id)));
        return menu;
    }

    // ---------------------------------------------------------------- photo operations

    private void NewPhoto_Click(object sender, RoutedEventArgs e) => AddPhoto();

    /// <summary>Saves the current camera as a new photo with a neutral image finish.</summary>
    private void AddPhoto()
    {
        if (_source is null) return;
        try
        {
            var names = _source.RenderScenes.Select(photo => photo.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var number = _source.RenderScenes.Count + 1;
            while (names.Contains($"Foto {number}")) number++;
            WatchPhotoEdits();
            var setup = CaptureCurrentSetup().WithoutEffects();
            _applyingPhoto = true;
            try { ApplySetupForProject(setup, _source.ProjectFilePath); }
            finally { _applyingPhoto = false; }
            var id = _source.AddRenderPhoto($"Foto {number}", setup, MakeThumbnail());
            _activePhotoId = id;
            _photoBaseline = setup;
            RefreshPhotos();
            StatusText.Text = $"Foto {number} guardada. Sus filtros se guardan solos mientras la tengas abierta.";
        }
        catch (InvalidDataException error) { StatusText.Text = LocalizedErrors.Translate(error.Message); }
    }

    private RenderSetup CaptureCurrentSetup() =>
        _source!.ProjectFilePath is { } path ? CaptureSetupForProject(_source.Camera, path) : CaptureSetup(_source.Camera);

    /// <summary>The panel's current look, but with the camera of the stored photo, to detect edits to filters only.</summary>
    private RenderSetup CurrentLookWithCameraOf(RenderSetup stored) => CaptureCurrentSetup() with
    {
        CameraX = stored.CameraX, CameraY = stored.CameraY, CameraZ = stored.CameraZ,
        YawDegrees = stored.YawDegrees, PitchDegrees = stored.PitchDegrees, FieldOfViewDegrees = stored.FieldOfViewDegrees
    };

    /// <summary>Opens a photo: its look is applied at once and the camera glides to its viewpoint.</summary>
    private void ActivatePhoto(Guid id, bool animate)
    {
        if (_source is null) return;
        if (_activePhotoId != id) WatchPhotoEdits();
        var photo = _source.RenderScenes.FirstOrDefault(item => item.Id == id);
        if (photo is null) return;
        _applyingPhoto = true;
        _activePhotoId = id;
        try
        {
            ApplySetupForProject(photo.Setup, _source.ProjectFilePath);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            StatusText.Text = LocalizedErrors.Translate(error.Message);
        }
        FlyTo(photo.Setup, animate);
        RefreshPhotos();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (_source is null || _activePhotoId != id) return;
            try { _photoBaseline = CurrentLookWithCameraOf(photo.Setup); }
            catch (InvalidDataException) { _photoBaseline = null; }
            _applyingPhoto = false;
            ScheduleStyledPreview();
        });
    }

    /// <summary>Glides the shared camera to a photo's viewpoint with an eased half-second flight.</summary>
    private void FlyTo(RenderSetup setup, bool animate)
    {
        if (_navigator is null || _source is null) return;
        _flightTimer?.Stop();
        var camera = _source.Camera;
        var targetPosition = new Vector3((float)setup.CameraX, (float)setup.CameraY, (float)setup.CameraZ);
        var targetYaw = (float)setup.YawDegrees;
        var targetPitch = (float)setup.PitchDegrees;
        var targetFov = (float)setup.FieldOfViewDegrees;
        var distance = Vector3.Distance(camera.Position, targetPosition);
        if (!animate || distance > 400)
        {
            _navigator.ApplyCamera(targetPosition, targetYaw, targetPitch, targetFov, recordHistory: true);
            return;
        }
        var startPosition = camera.Position;
        var startYaw = camera.YawDegrees;
        var startPitch = camera.PitchDegrees;
        var startFov = camera.FieldOfViewDegrees;
        var turn = ((targetYaw - startYaw + 540) % 360) - 180;
        var started = DateTime.UtcNow;
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(300 + distance * 25, 350, 900));
        _navigator.ApplyCamera(startPosition, startYaw, startPitch, startFov, recordHistory: true);
        _flightTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _flightTimer.Tick += (_, _) =>
        {
            var t = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            var eased = (float)(t * t * (3 - 2 * t));
            var done = t >= 1;
            _navigator.ApplyCamera(done ? targetPosition : Vector3.Lerp(startPosition, targetPosition, eased),
                done ? targetYaw : startYaw + turn * eased, done ? targetPitch : startPitch + (targetPitch - startPitch) * eased,
                done ? targetFov : startFov + (targetFov - startFov) * eased, recordHistory: done);
            if (done) _flightTimer?.Stop();
        };
        _flightTimer.Start();
    }

    /// <summary>Stores the current camera (and thumbnail) in a photo; its filters stay its own.</summary>
    private void UpdatePhotoView(Guid id)
    {
        if (_source is null) return;
        var camera = _source.Camera;
        var position = camera.Position;
        float yaw = camera.YawDegrees, pitch = camera.PitchDegrees, fov = camera.FieldOfViewDegrees;
        if (_activePhotoId != id)
        {
            ActivatePhoto(id, animate: false);
            _navigator?.ApplyCamera(position, yaw, pitch, fov, recordHistory: false);
        }
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (_source is null) return;
            try
            {
                var setup = CaptureCurrentSetup();
                _source.UpdateRenderPhoto(id, setup, thumbnail: MakeThumbnail());
                _activePhotoId = id;
                _photoBaseline = setup;
                RefreshPhotos();
                StatusText.Text = "Encuadre de la foto actualizado.";
            }
            catch (InvalidDataException error) { StatusText.Text = LocalizedErrors.Translate(error.Message); }
        });
    }

    private void RenamePhoto(Guid id)
    {
        var photo = _source?.RenderScenes.FirstOrDefault(item => item.Id == id);
        if (photo is null || _source is null) return;
        var name = PromptWindow.Ask(Window.GetWindow(this), UiText.Get("RenderPanel.SceneName"), photo.Name)?.Trim();
        if (string.IsNullOrEmpty(name) || name == photo.Name) return;
        try
        {
            _source.UpdateRenderPhoto(id, name: name);
            RefreshPhotos();
        }
        catch (InvalidDataException error) { StatusText.Text = LocalizedErrors.Translate(error.Message); }
    }

    private void DuplicatePhoto(Guid id)
    {
        if (_source is null) return;
        var index = _source.RenderScenes.ToList().FindIndex(item => item.Id == id);
        if (index < 0) return;
        var photo = _source.RenderScenes[index];
        try
        {
            _source.AddRenderPhoto((photo.Name + " copia").Length > 120 ? photo.Name : photo.Name + " copia", photo.Setup, photo.Thumbnail, index + 1);
            RefreshPhotos();
        }
        catch (InvalidDataException error) { StatusText.Text = LocalizedErrors.Translate(error.Message); }
    }

    private void DeletePhoto(Guid id)
    {
        if (_source is null) return;
        try
        {
            _source.DeleteRenderPhoto(id);
            if (_activePhotoId == id) { _activePhotoId = null; _photoBaseline = null; }
            RefreshPhotos();
        }
        catch (InvalidDataException error) { StatusText.Text = LocalizedErrors.Translate(error.Message); }
    }

    /// <summary>Saves filter edits into the open photo, and repaints the styled still when the look changed.</summary>
    private void WatchPhotoEdits()
    {
        if (_source is null || !IsVisible || _applyingPhoto || _rendering || _batchRunning) return;
        CheckStyledPreviewStale();
        if (_activePhotoId is not { } id) return;
        var photo = _source.RenderScenes.FirstOrDefault(item => item.Id == id);
        if (photo is null)
        {
            _activePhotoId = null;
            return;
        }
        try
        {
            var current = CurrentLookWithCameraOf(photo.Setup);
            if (_photoBaseline is null || current == _photoBaseline)
            {
                _photoBaseline ??= current;
                return;
            }
            _photoBaseline = current;
            _source.UpdateRenderPhoto(id, current, thumbnail: MakeThumbnail());
            RefreshPhotos();
        }
        catch (InvalidDataException) { }
    }

    // ---------------------------------------------------------------- keyboard (Ctrl+1…0 saves, Shift+1…0 opens)

    private static int SlotOf(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D1,
        Key.D0 => 9,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1,
        Key.NumPad0 => 9,
        _ => -1
    };

    private void PhotoKeyDown(object sender, KeyEventArgs e)
    {
        var slot = SlotOf(e.Key);
        if (slot < 0 || _source is null || _navigator is not { IsActive: true } || _rendering || _batchRunning) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl == shift) return;
        e.Handled = true;
        var photos = _source.RenderScenes;
        if (shift)
        {
            if (slot < photos.Count) ActivatePhoto(photos[slot].Id, animate: true);
            return;
        }
        if (slot < photos.Count) UpdatePhotoView(photos[slot].Id);
        else AddPhoto();
    }
}
