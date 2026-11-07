using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>
/// Physical camera settings and what they mean for the render: the exposure multiplier from ISO, shutter and aperture,
/// and the lens opening that blurs what is out of focus. Frame size is given as a sensor; a full-frame sensor is 36 × 24 mm.
/// </summary>
public static class PhotographicCamera
{
    /// <summary>Exposure value (at ISO 100) of the "Sunny 16" rule: f/16 at 1/100 s under a clear sky.</summary>
    public static readonly double SunnySixteenEv = Math.Log2(16.0 * 16.0 / 0.01);

    /// <summary>
    /// The renderer's default exposure multiplier, which renders a sunlit mid-grey surface as mid-grey. It is the value
    /// that Sunny 16 corresponds to, so every physical setting is expressed relative to it.
    /// </summary>
    public const float SunnySixteenExposure = 0.4f;

    /// <summary>Exposure value at ISO 100 of an f-number and shutter time; each +1 halves the light reaching the sensor.</summary>
    public static double ExposureValue(double fNumber, double shutterSeconds, double iso = 100)
    {
        Validate(fNumber, shutterSeconds, iso);
        // Logarithms avoid overflow/underflow in the intermediate f-number squared or ISO ratio.
        return 2 * Math.Log2(fNumber) - Math.Log2(shutterSeconds) + Math.Log2(100) - Math.Log2(iso);
    }

    /// <summary>
    /// The value for <see cref="RenderSettings.Exposure"/> that mimics a camera set to the given values. Sunny 16 gives
    /// <see cref="SunnySixteenExposure"/>; each stop of extra light on the sensor doubles it.
    /// </summary>
    public static float Exposure(double fNumber, double shutterSeconds, double iso = 100, double compensationStops = 0)
    {
        if (!double.IsFinite(compensationStops))
            throw new ArgumentOutOfRangeException(nameof(compensationStops));
        var stops = SunnySixteenEv - ExposureValue(fNumber, shutterSeconds, iso) + compensationStops;
        return (float)(SunnySixteenExposure * Math.Pow(2, Math.Clamp(stops, -12, 16)));
    }

    /// <summary>Vertical field of view in degrees for a focal length and sensor height (both in millimeters).</summary>
    public static float VerticalFieldOfView(double focalLengthMillimeters, double sensorHeightMillimeters = 24)
    {
        if (!double.IsFinite(focalLengthMillimeters) || focalLengthMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(focalLengthMillimeters));
        if (!double.IsFinite(sensorHeightMillimeters) || sensorHeightMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(sensorHeightMillimeters));
        var angle = (float)(2 * Math.Atan((sensorHeightMillimeters / focalLengthMillimeters) / 2) * 180 / Math.PI);
        if (!float.IsFinite(angle) || angle <= 0 || angle >= 180)
            throw new ArgumentOutOfRangeException(nameof(focalLengthMillimeters), "The lens must produce a representable field of view.");
        return angle;
    }

    /// <summary>Focal length in millimeters that gives a vertical field of view on a sensor.</summary>
    public static double FocalLength(double verticalFieldOfViewDegrees, double sensorHeightMillimeters = 24)
    {
        if (!double.IsFinite(verticalFieldOfViewDegrees) || verticalFieldOfViewDegrees <= 0 || verticalFieldOfViewDegrees >= 180)
            throw new ArgumentOutOfRangeException(nameof(verticalFieldOfViewDegrees));
        if (!double.IsFinite(sensorHeightMillimeters) || sensorHeightMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(sensorHeightMillimeters));
        var focalLength = sensorHeightMillimeters / (2 * Math.Tan(verticalFieldOfViewDegrees * Math.PI / 360));
        if (!double.IsFinite(focalLength) || focalLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(verticalFieldOfViewDegrees));
        return focalLength;
    }

    /// <summary>Radius of the lens opening in meters: focal length over twice the f-number.</summary>
    public static float ApertureRadius(double focalLengthMillimeters, double fNumber)
    {
        if (!double.IsFinite(focalLengthMillimeters) || focalLengthMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(focalLengthMillimeters));
        if (!double.IsFinite(fNumber) || fNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(fNumber));
        var radius = (float)((focalLengthMillimeters / fNumber) / 2000);
        if (!float.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(focalLengthMillimeters), "The lens radius must be representable.");
        return radius;
    }

    /// <summary>
    /// Describes a physical camera: field of view and depth of field follow from the focal length, sensor and f-number,
    /// so a wide aperture blurs the background and a small one keeps the whole building sharp.
    /// </summary>
    public static RenderCamera Create(Vector3 position, Vector3 target, double focalLengthMillimeters, double fNumber,
        double focusDistanceMeters = 0, double sensorHeightMillimeters = 24, bool correctVerticals = true)
    {
        if (!double.IsFinite(focusDistanceMeters) || focusDistanceMeters < 0 || focusDistanceMeters > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(focusDistanceMeters));
        if (!Finite(position) || !Finite(target) || position == target)
            throw new ArgumentException("Camera position and target must be distinct finite points.", nameof(target));
        return new RenderCamera(position, target, VerticalFieldOfView(focalLengthMillimeters, sensorHeightMillimeters))
        {
            ApertureRadius = ApertureRadius(focalLengthMillimeters, fNumber),
            FocusDistance = (float)focusDistanceMeters,
            CorrectVerticals = correctVerticals
        };
    }

    /// <summary>
    /// Diameter in meters of the blur circle on the sensor-side scale for a point at a distance, given the focus distance
    /// (thin-lens model). Zero at the plane of focus; useful for checking depth of field in tests and in the interface.
    /// </summary>
    public static double BlurCircleDiameter(double focalLengthMillimeters, double fNumber, double focusMeters, double subjectMeters)
    {
        if (!double.IsFinite(focalLengthMillimeters) || focalLengthMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(focalLengthMillimeters));
        if (!double.IsFinite(fNumber) || fNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(fNumber));
        if (!double.IsFinite(focusMeters) || focusMeters <= focalLengthMillimeters / 1000)
            throw new ArgumentOutOfRangeException(nameof(focusMeters), "Focus must lie beyond the focal length.");
        if (!double.IsFinite(subjectMeters) || subjectMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(subjectMeters));
        var f = focalLengthMillimeters / 1000;
        var aperture = f / fNumber;
        var diameter = aperture * f * Math.Abs(subjectMeters - focusMeters) / (subjectMeters * (focusMeters - f));
        if (!double.IsFinite(diameter)) throw new ArgumentOutOfRangeException(nameof(subjectMeters));
        return diameter;
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static void Validate(double fNumber, double shutterSeconds, double iso)
    {
        if (!double.IsFinite(fNumber) || fNumber <= 0) throw new ArgumentOutOfRangeException(nameof(fNumber));
        if (!double.IsFinite(shutterSeconds) || shutterSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(shutterSeconds));
        if (!double.IsFinite(iso) || iso <= 0) throw new ArgumentOutOfRangeException(nameof(iso));
    }
}
