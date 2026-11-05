using System.Numerics;
using System.Windows;
using System.Windows.Input;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

internal sealed partial class SceneNavigator
{
    private bool _flightGesture;

    /// <summary>Explicit metres per second; null preserves project-scaled modeling navigation.</summary>
    public float? TravelSpeed { get; set; }

    /// <summary>Keeps forward travel level while allowing Q/E to change floors.</summary>
    public bool HorizontalTravel { get; set; }

    /// <summary>
    /// Lumion-style controls: the left button walks forward/back and turns, the right button looks, the middle button
    /// strafes, Q rises and E descends, Space slows and Shift speeds the flight (Shift+Space is very fast).
    /// </summary>
    public bool LumionControls { get; set; }

    /// <summary>Lets wheel travel cross picked surfaces instead of approaching them asymptotically.</summary>
    public bool FreeTravel { get; set; }

    /// <summary>Groups continuous keyboard movement into one view-history entry and applies metric speed.</summary>
    private void ApplyFlight(Vector3 input, float seconds, bool fast, bool precise) =>
        ApplyFlightScaled(input, seconds, fast, precise, null);

    /// <summary>Flight with an optional explicit speed multiplier that replaces the fast and precise flags.</summary>
    private void ApplyFlightScaled(Vector3 input, float seconds, bool fast, bool precise, float? multiplier)
    {
        if (input == Vector3.Zero)
        {
            EndFlight();
            return;
        }
        if (!_flightGesture)
        {
            _history.Record(Camera);
            _flightGesture = true;
        }
        var speed = TravelSpeed ?? Math.Clamp(SceneRadius() * 0.25f, 1.5f, 50f);
        MoveCamera(multiplier is { } scale
            ? CameraTravel.Displacement(Camera, input, speed, seconds, HorizontalTravel, scale)
            : CameraTravel.Displacement(Camera, input, speed, seconds, HorizontalTravel, fast, precise));
        Changed?.Invoke();
    }

    /// <summary>Commits flight history on release, focus loss, hiding, or camera locking.</summary>
    private void EndFlight()
    {
        if (!_flightGesture) return;
        _flightGesture = false;
        _history.Record(Camera);
    }

    /// <summary>
    /// Places the shared camera exactly (used when a photo is opened) and notifies the views. The caller may skip the
    /// history entry for the intermediate frames of a camera flight.
    /// </summary>
    public void ApplyCamera(Vector3 position, float yawDegrees, float pitchDegrees, float fieldOfViewDegrees, bool recordHistory)
    {
        if (recordHistory) _history.Record(Camera);
        Camera.Position = position;
        Camera.YawDegrees = yawDegrees;
        Camera.PitchDegrees = Math.Clamp(pitchDegrees, -89f, 89f);
        Camera.FieldOfViewDegrees = Math.Clamp(fieldOfViewDegrees, 8f, 110f);
        Changed?.Invoke();
        if (recordHistory) _history.Record(Camera);
    }
}
