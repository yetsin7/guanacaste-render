using System.IO;
using System.Windows.Threading;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Keeps the movie (camera marks and layer keys) in the project. Edits are written a moment after they stop, so dragging a
/// mark makes one saved step and not hundreds; opening a project loads its movie, or clears the previous project's.
/// </summary>
public partial class RenderPanel
{
    private static readonly TimeSpan MovieSaveDelay = TimeSpan.FromSeconds(1.5);
    private DispatcherTimer? _movieSaveTimer;
    private string? _savedMovie;
    private bool _restoringMovie;

    /// <summary>Writes the movie to the project shortly after the last change.</summary>
    private void ScheduleMovieSave()
    {
        if (_restoringMovie || _source is null) return;
        _movieSaveTimer ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = MovieSaveDelay };
        _movieSaveTimer.Tick -= MovieSaveTick;
        _movieSaveTimer.Tick += MovieSaveTick;
        _movieSaveTimer.Stop();
        _movieSaveTimer.Start();
    }

    private void MovieSaveTick(object? sender, EventArgs e) => SaveMovieNow();

    /// <summary>Saves at once what is waiting, so closing or switching project never loses the last edits.</summary>
    internal void SaveMovieNow()
    {
        _movieSaveTimer?.Stop();
        if (_source is null || _restoringMovie) return;
        var text = MovieStorage.Serialize(_path, _layers);
        if (text == _savedMovie) return;
        try
        {
            _source.SetExtension(MovieStorage.Key, text);
            _savedMovie = text;
        }
        catch (InvalidDataException)
        {
            StatusText.Text = "La película es demasiado grande para guardarla en el proyecto.";
        }
    }

    /// <summary>Loads the movie of the project that was just opened; an empty or unreadable one leaves a blank movie.</summary>
    internal void RestoreMovie()
    {
        if (_source is null) return;
        _movieSaveTimer?.Stop();
        var text = _source.GetExtension(MovieStorage.Key);
        _restoringMovie = true;
        try
        {
            StopTour();
            MovieStorage.TryParse(text, out var path, out var layers);
            _path.Clear();
            _path.Ease = path.Ease;
            foreach (var mark in path.Marks) _path.Add(mark);
            _layers.Clear();
            foreach (var key in layers.Keys) _layers.Set(key.LevelId, key.Seconds, key.Visible);
            LayersAnimationCheck.IsChecked = !_layers.IsEmpty;
            if (_layers.IsEmpty) _fxActive.Remove("layers"); else _fxActive.Add("layers");
            _playTime = 0;
            _savedMovie = text;
            RefreshMarks();
            RefreshFxStack();
            RedrawTimeline();
        }
        finally { _restoringMovie = false; }
        _layerState = "";
        ApplyLayerPreview(0);
    }
}
