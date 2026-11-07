namespace Guanacaste3D.Core.Navigation;

/// <summary>Bounded camera-only undo and redo, independent of document editing history.</summary>
public sealed class CameraViewHistory
{
    private readonly List<NavigationCamera> _views = [];
    private int _index = -1;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index + 1 < _views.Count;

    /// <summary>Starts a fresh document's history without retaining views from another project.</summary>
    public void Reset(NavigationCamera camera)
    {
        _views.Clear();
        _index = -1;
        Record(camera);
    }

    /// <summary>Stores a distinct completed view and discards any abandoned forward branch.</summary>
    public void Record(NavigationCamera camera)
    {
        if (_index >= 0 && Equivalent(_views[_index], camera)) return;
        if (CanGoForward) _views.RemoveRange(_index + 1, _views.Count - _index - 1);
        _views.Add(camera.Clone());
        if (_views.Count > 80) _views.RemoveAt(0);
        _index = _views.Count - 1;
    }

    /// <summary>Restores an adjacent camera snapshot without modifying geometry or recording a new branch.</summary>
    public bool Restore(NavigationCamera camera, bool forward)
    {
        if (forward ? !CanGoForward : !CanGoBack) return false;
        _index += forward ? 1 : -1;
        var saved = _views[_index];
        camera.Position = saved.Position;
        camera.YawDegrees = saved.YawDegrees;
        camera.PitchDegrees = saved.PitchDegrees;
        camera.FieldOfViewDegrees = saved.FieldOfViewDegrees;
        return true;
    }

    /// <summary>Ignores floating-point noise so redraws do not consume history slots.</summary>
    private static bool Equivalent(NavigationCamera first, NavigationCamera second) =>
        System.Numerics.Vector3.DistanceSquared(first.Position, second.Position) < 1e-10f &&
        Math.Abs(first.YawDegrees - second.YawDegrees) < 1e-4f &&
        Math.Abs(first.PitchDegrees - second.PitchDegrees) < 1e-4f &&
        Math.Abs(first.FieldOfViewDegrees - second.FieldOfViewDegrees) < 1e-4f;
}
