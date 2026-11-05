namespace Guanacaste3D.Core;

/// <summary>Model viewport presentations that do not rewrite category material assignments.</summary>
public enum ModelDisplayStyle { Wireframe, HiddenLine, Shaded, ConsistentColors, Textures, Realistic }

/// <summary>Normalized screen crop that remains stable when the application window changes size.</summary>
public sealed record ViewCropRegion(double X, double Y, double Width, double Height);

/// <summary>Camera orientation saved together with a locked model view.</summary>
public sealed record ViewCameraPose(float X, float Y, float Z, float Yaw, float Pitch);

/// <summary>Persistent graphics for one model view; temporary hiding and reveal mode are intentionally session-only.</summary>
public sealed record ViewGraphicsSettings
{
    public ModelDisplayStyle Style { get; init; } = ModelDisplayStyle.Textures;
    public Geometry.ModelDetailLevel Detail { get; init; } = Geometry.ModelDetailLevel.Medium;
    public string SunPreset { get; init; } = "Noon";
    public bool Shadows { get; init; }
    public double Ambient { get; init; } = 0.62;
    public string Background { get; init; } = "White";
    public float FieldOfView { get; init; } = 45;
    public bool ShowEdges { get; init; } = true;

    /// <summary>Revit's "Show line weights": true draws edges at their full model thickness, false draws them hairline-thin.</summary>
    public bool ThickLines { get; init; } = true;
    public bool CameraLocked { get; init; }
    public ViewCameraPose? CameraPose { get; init; }
    public bool CropActive { get; init; }
    public bool CropBoundaryVisible { get; init; } = true;
    public ViewCropRegion? Crop { get; init; }

    /// <summary>Rejects invalid saved graphics before they can affect projection or WPF layout.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Style) || !Enum.IsDefined(Detail) || SunPreset is not ("Off" or "Morning" or "Noon" or "Afternoon") ||
            Background is not ("White" or "Gray" or "Sky") || !double.IsFinite(Ambient) || Ambient is < 0.15 or > 1 ||
            !float.IsFinite(FieldOfView) || FieldOfView is < 15 or > 100)
            throw new InvalidDataException("The view graphics settings are invalid.");
        if (Crop is { } crop && (!double.IsFinite(crop.X + crop.Y + crop.Width + crop.Height) ||
            crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 ||
            crop.X + crop.Width > 1.000001 || crop.Y + crop.Height > 1.000001))
            throw new InvalidDataException("The view crop region is invalid.");
        if (CameraPose is { } pose && (!float.IsFinite(pose.X + pose.Y + pose.Z + pose.Yaw + pose.Pitch) || pose.Pitch is < -90 or > 90))
            throw new InvalidDataException("The saved view camera is invalid.");
    }
}
