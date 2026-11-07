namespace Guanacaste3D.Core.Rendering;

/// <summary>Copies image finishing settings independently of a photo's camera and environment.</summary>
public static class RenderPhotoEffects
{
    /// <summary>Creates a neutral look while retaining the current viewpoint and scene lighting.</summary>
    public static RenderSetup WithoutEffects(this RenderSetup setup) => setup.WithEffectsFrom(new RenderSetup { Bloom = false });

    /// <summary>Copies the complete image finish without moving the destination camera or changing its sky.</summary>
    public static RenderSetup WithEffectsFrom(this RenderSetup destination, RenderSetup source) => destination with
    {
        StyleId = source.StyleId, Look = source.Look, ExposureStops = source.ExposureStops,
        Temperature = source.Temperature, AutoExposure = source.AutoExposure, Bloom = source.Bloom,
        Vignette = source.Vignette, Highlights = source.Highlights, Shadows = source.Shadows,
        ContrastAdjust = source.ContrastAdjust, SaturationAdjust = source.SaturationAdjust,
        Vibrance = source.Vibrance, Tint = source.Tint, Glare = source.Glare, Fog = source.Fog,
        Sharpen = source.Sharpen, ChromaticAberration = source.ChromaticAberration,
        FilmGrain = source.FilmGrain, Monochrome = source.Monochrome,
        HighlightMaterial = source.HighlightMaterial, HighlightDim = source.HighlightDim,
        Rain = source.Rain, Snow = source.Snow, Wetness = source.Wetness, Grass = source.Grass, Wind = source.Wind,
        LensFlare = source.LensFlare, LightRays = source.LightRays, TiltShift = source.TiltShift, TiltShiftPosition = source.TiltShiftPosition,
        LutPresetId = source.LutPresetId, LutPath = source.LutPath, LutStrength = source.LutStrength
    };
}
