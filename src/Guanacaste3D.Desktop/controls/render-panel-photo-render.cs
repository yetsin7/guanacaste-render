using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>Renders photos one by one or all at once, saving each picture as soon as it is finished.</summary>
public partial class RenderPanel
{
    private bool _batchRunning;
    private bool _batchCancel;
    private string? _batchLabel;

    private void RenderPhotos_Click(object sender, RoutedEventArgs e) => OpenRenderPhotosDialog(null);

    /// <summary>Folder next to the project (project.g3d.renders\Fotos), or a local draft folder for unsaved projects.</summary>
    private string DefaultRenderFolder()
    {
        var vault = AiRenderVault.GetVaultDirectory(_source?.ProjectFilePath, _source?.ProjectName ?? "render");
        return Path.Combine(vault, "Fotos");
    }

    /// <summary>Opens the render window; <paramref name="only"/> starts with just that photo marked.</summary>
    internal void OpenRenderPhotosDialog(Guid? only)
    {
        if (_source is null || _rendering || _renderingTour || _batchRunning) return;
        var dialog = new RenderPhotosDialog([.. _source.RenderScenes], only ?? (_source.RenderScenes.Count > 1 ? null : _activePhotoId),
            DefaultRenderFolder()) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result is not { } options) return;
        _ = RenderPhotosAsync(options);
    }

    /// <summary>Loads a photo's camera and look for rendering, waiting for an HDRI sky if the photo uses one.</summary>
    private async Task ApplyPhotoForRenderAsync(RenderSceneBookmark photo)
    {
        if (_source is null) return;
        _applyingPhoto = true;
        try
        {
            var setup = photo.Setup;
            _navigator?.ApplyCamera(new System.Numerics.Vector3((float)setup.CameraX, (float)setup.CameraY, (float)setup.CameraZ),
                (float)setup.YawDegrees, (float)setup.PitchDegrees, (float)setup.FieldOfViewDegrees, recordHistory: false);
            ApplySetupForProject(setup, _source.ProjectFilePath);
            await Task.Delay(150);
            for (var wait = 0; setup.EnvironmentPath is not null && _hdriEnvironment is null && wait < 50; wait++)
                await Task.Delay(100);
            _activePhotoId = photo.Id;
            _photoBaseline = CurrentLookWithCameraOf(setup);
        }
        finally { _applyingPhoto = false; }
        RefreshPhotos();
    }

    private static string UniquePath(string folder, string stem, string extension)
    {
        var path = Path.Combine(folder, stem + extension);
        for (var copy = 2; File.Exists(path); copy++) path = Path.Combine(folder, $"{stem} ({copy}){extension}");
        return path;
    }

    private async Task RenderPhotosAsync(PhotoRenderOptions options)
    {
        if (_source is null) return;
        var source = _source;
        var original = CaptureCurrentSetup();
        var originalPhoto = _activePhotoId;
        var folder = options.Folder;
        var targets = options.PhotoIds.Count == 0 ? new Guid?[] { null } : [.. options.PhotoIds.Select(id => (Guid?)id)];
        var saved = new List<string>();
        _batchRunning = true;
        _batchCancel = false;
        string? failure = null;
        try
        {
            try { Directory.CreateDirectory(folder); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                folder = AiRenderVault.GetVaultDirectory(null, source.ProjectName);
                Directory.CreateDirectory(folder);
            }
            for (var index = 0; index < targets.Length && !_batchCancel; index++)
            {
                var photo = targets[index] is { } id ? source.RenderScenes.FirstOrDefault(item => item.Id == id) : null;
                if (targets[index] is not null && photo is null) continue;
                var name = photo?.Name ?? "Vista actual";
                _batchLabel = targets.Length > 1 ? $"Foto {index + 1} de {targets.Length} · {name}" : name;
                StatusText.Text = _batchLabel;
                if (photo is not null) await ApplyPhotoForRenderAsync(photo);
                Select(SizePicker, options.Size);
                Select(QualityPicker, options.Quality);
                // The batch's size and quality are not part of the photo: do not let the watcher store them in it.
                if (photo is not null) _photoBaseline = CurrentLookWithCameraOf(photo.Setup);
                for (var attempt = 0; attempt < 2 && !_batchCancel; attempt++)
                {
                    _lastFinal = null;
                    await RenderFinalAsync();
                    if (_lastFinal is not null) break;
                    await Task.Delay(500);
                }
                if (_batchCancel || _lastFinal is null) continue;
                var extension = options.Jpeg ? ".jpg" : ".png";
                var path = UniquePath(folder, SafeName($"{source.ProjectName} - {index + 1:00} {name}"), extension);
                BitmapEncoder encoder = options.Jpeg ? new JpegBitmapEncoder { QualityLevel = 95 } : new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(CurrentImage(opaque: options.Jpeg)!));
                await using (var stream = File.Create(path)) encoder.Save(stream);
                saved.Add(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            failure = error.Message;
        }
        finally
        {
            _batchRunning = false;
            _batchLabel = null;
        }

        // After a batch the camera and filters go back to what they were, so the next photo is not affected;
        // after a single render the finished picture stays on screen. This depends on the number of requested
        // photos, not the number successfully saved (failed/cancelled batches must restore the view too).
        if (targets.Length > 1 && ReferenceEquals(source, _source))
        {
            _applyingPhoto = true;
            _navigator?.ApplyCamera(new System.Numerics.Vector3((float)original.CameraX, (float)original.CameraY, (float)original.CameraZ),
                (float)original.YawDegrees, (float)original.PitchDegrees, (float)original.FieldOfViewDegrees, recordHistory: false);
            ApplySetupForProject(original, source.ProjectFilePath);
            _activePhotoId = originalPhoto;
            _applyingPhoto = false;
            _photoBaseline = null;
            RefreshPhotos();
        }
        StatusText.Text = failure is not null
            ? $"No se pudo guardar el render: {failure}"
            : _batchCancel
                ? $"Render detenido. {saved.Count} imagen(es) guardada(s) en {folder}"
                : saved.Count == 0
                    ? "No se generó ningún render."
                    : $"{saved.Count} render(es) guardado(s) en {folder}";
        if (options.OpenFolder && saved.Count > 0)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{saved[^1]}\"") { UseShellExecute = true }); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
    }
}
