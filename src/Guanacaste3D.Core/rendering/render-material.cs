using System.Numerics;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Procedural surface detail evaluated from world position, so imported meshes need no texture coordinates.</summary>
public enum SurfacePattern : byte
{
    None,
    /// <summary>Two-tone squares one meter wide, used for the ground so scale reads on screen.</summary>
    Checker,
    /// <summary>Mottled green-brown breakup for lawns and terrain.</summary>
    Grass,
    /// <summary>Ceramic tiles with darker joints.</summary>
    Tiles,
    /// <summary>Long boards with slight tone variation.</summary>
    Planks,
    /// <summary>Masonry blocks in running bond.</summary>
    Blocks,
    /// <summary>Fine mottling for poured concrete and plaster.</summary>
    Concrete,
    /// <summary>Clusters of leaves: light and dark clumps with a strong, lumpy relief.</summary>
    Foliage,
    /// <summary>Furrowed bark running up a trunk.</summary>
    Bark,
    /// <summary>Clay barrel tiles in overlapping courses, laid down the slope of the surface.</summary>
    RoofTiles,
    /// <summary>Leaf-to-leaf color variation: fresh, mature and a few yellowing leaves.</summary>
    Leaves,
    /// <summary>Woven textile: a fine basket weave with a soft nap.</summary>
    Fabric,
    /// <summary>Weathered asphalt with fine aggregate and irregular surface variation.</summary>
    Asphalt,
    /// <summary>Subtle mineral variation and pores on exposed cast concrete.</summary>
    PouredConcrete,
    /// <summary>Irregular coursed natural-stone masonry with individually toned stones, recessed mortar and mineral relief.</summary>
    StoneMasonry,
    /// <summary>Small, calm ripples for a pool or a basin.</summary>
    Ripples,
    /// <summary>Open-sea waves of several lengths, with white caps on the crests.</summary>
    Waves,
    /// <summary>Fine parallel lines along one direction, as left on steel by a wire brush.</summary>
    Brushed,
    /// <summary>A 2×2 twill weave of carbon fibre under clear lacquer.</summary>
    CarbonFiber,
    /// <summary>A sheet with staggered round holes, a metal grill.</summary>
    Perforated,
    /// <summary>Blue paint flaking off to show orange-brown rust.</summary>
    RustPaint,
    /// <summary>Green verdigris streaks and grime, as on old copper.</summary>
    Patina,
    /// <summary>Black rubber faded and scuffed by use.</summary>
    WornRubber
}

/// <summary>
/// A physically based surface description shared by the BIM scene and imported models.
/// Colors are linear RGB in 0–1; <see cref="Emission"/> is radiance and may exceed 1.
/// </summary>
public sealed record RenderMaterial
{
    public string Name { get; init; } = "Material";
    /// <summary>Diffuse reflectance for dielectrics, specular color for metals.</summary>
    public Vector3 BaseColor { get; init; } = new(0.7f);
    /// <summary>Microfacet roughness: 0 is a mirror, 1 fully matte.</summary>
    public float Roughness { get; init; } = 0.8f;
    public float Metallic { get; init; }
    /// <summary>Index of refraction; sets the reflectance at normal incidence of non-metals.</summary>
    public float Ior { get; init; } = 1.5f;
    /// <summary>Light emitted by the surface; non-zero makes it a light source.</summary>
    public Vector3 Emission { get; init; }
    /// <summary>
    /// 0 for opaque surfaces, 1 for clear thin glass. Fractions behave as the share of rays that pass,
    /// which matches an alpha-tested or tinted film.
    /// </summary>
    public float Transmission { get; init; }
    /// <summary>Color multiplied into light that passes through a transmissive surface.</summary>
    public Vector3 TransmissionTint { get; init; } = new(0.86f, 0.92f, 0.94f);
    public SurfacePattern Pattern { get; init; }
    /// <summary>Pattern size multiplier; 1 matches real-world dimensions in meters.</summary>
    public float PatternScale { get; init; } = 1;
    /// <summary>
    /// An image projected in world space onto the surface; it multiplies <see cref="BaseColor"/> and replaces the
    /// procedural pattern. Null for untextured materials.
    /// </summary>
    public RenderTexture? Texture { get; init; }
    /// <summary>Meters covered by one copy of <see cref="Texture"/>.</summary>
    public float TextureSize { get; init; } = 1;
    /// <summary>0–1: how strongly the texture's brightness raises the surface (light parts proud, dark parts recessed).</summary>
    public float TextureRelief { get; init; }
    /// <summary>
    /// 0–1: share of the diffuse light that passes through to the other side, as through a leaf or a paper lantern.
    /// Thin surfaces only; it makes foliage glow when the sun is behind it.
    /// </summary>
    public float Translucency { get; init; }
    /// <summary>
    /// The texture is mapped with the mesh's own texture coordinates (a modelled leaf, bark or car) instead of being
    /// projected in world space; one coordinate unit is one copy of the image.
    /// </summary>
    public bool UvMapped { get; init; }
    /// <summary>
    /// 0 for an opaque surface; otherwise the texture coverage below which the surface is a hole (leaf and grass cards),
    /// for camera rays and shadows alike.
    /// </summary>
    public float AlphaCutoff { get; init; }
    /// <summary>
    /// The relief of a <see cref="UvMapped"/> surface (bark furrows, leaf veins) as a tangent-space normal map; null for
    /// a smooth surface.
    /// </summary>
    public NormalMap? NormalMap { get; init; }
    /// <summary>Optional linear grayscale roughness, sampled in UV or projected world coordinates.</summary>
    public RenderScalarMap? RoughnessMap { get; init; }
    /// <summary>Optional linear grayscale metalness; black is dielectric and white is metal.</summary>
    public RenderScalarMap? MetallicMap { get; init; }
    /// <summary>Optional linear height channel; bumps shading normals without moving BIM geometry.</summary>
    public RenderScalarMap? HeightMap { get; init; }

    public bool IsEmissive => Emission.X > 0 || Emission.Y > 0 || Emission.Z > 0;
    public bool IsTransmissive => Transmission > 0.001f;

    /// <summary>Reflectance at normal incidence for a dielectric of this index of refraction.</summary>
    public float DielectricF0
    {
        get
        {
            var ratio = (Ior - 1) / (Ior + 1);
            return ratio * ratio;
        }
    }

    /// <summary>Returns the same material with every value forced into a range the integrator can trust.</summary>
    public RenderMaterial Sanitized() => this with
    {
        BaseColor = Clean(BaseColor, 0, 1, 0.7f),
        Roughness = Clean(Roughness, 0.02f, 1, 0.8f),
        Metallic = Clean(Metallic, 0, 1, 0),
        Ior = Clean(Ior, 1.0f, 3.0f, 1.5f),
        Transmission = Clean(Transmission, 0, 1, 0),
        Emission = Clean(Emission, 0, 10000, 0),
        TransmissionTint = Clean(TransmissionTint, 0, 1, 1),
        PatternScale = Clean(PatternScale, 0.01f, 1000, 1),
        TextureSize = Clean(TextureSize, 0.01f, 1000, 1),
        TextureRelief = Clean(TextureRelief, 0, 1, 0),
        AlphaCutoff = Texture?.HasAlpha == true ? Clean(AlphaCutoff, 0, 1, 0) : 0,
        Translucency = Clean(Translucency, 0, 1, 0)
    };

    private static float Clean(float value, float low, float high, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, low, high) : fallback;

    private static Vector3 Clean(Vector3 value, float low, float high, float fallback) =>
        new(Clean(value.X, low, high, fallback), Clean(value.Y, low, high, fallback), Clean(value.Z, low, high, fallback));
}

/// <summary>Default materials for the BIM surface categories, plus common presets for imported geometry.</summary>
public static class MaterialLibrary
{
    /// <summary>Returns the stock material that renders a BIM surface category.</summary>
    public static RenderMaterial ForKind(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Wall => new RenderMaterial
        {
            Name = "Pared", BaseColor = new(0.80f, 0.78f, 0.73f), Roughness = 0.92f,
            Pattern = SurfacePattern.Concrete, PatternScale = 1.0f
        },
        SurfaceKind.Floor => new RenderMaterial
        {
            Name = "Piso", BaseColor = new(0.56f, 0.55f, 0.52f), Roughness = 0.38f,
            Pattern = SurfacePattern.Tiles, PatternScale = 0.6f
        },
        SurfaceKind.Water => new RenderMaterial
        {
            Name = "Agua", BaseColor = new(0.015f, 0.05f, 0.06f), Roughness = 0.01f, Ior = 1.33f,
            Pattern = SurfacePattern.Ripples, PatternScale = 0.12f
        },
        SurfaceKind.Ocean => new RenderMaterial
        {
            Name = "Océano", BaseColor = new(0.010f, 0.125f, 0.150f), Roughness = 0.02f, Ior = 1.33f,
            Pattern = SurfacePattern.Waves, PatternScale = 1f
        },
        // A clear coat over the pigment: the low roughness gives cars their sharp sky and tree reflections.
        SurfaceKind.CarPaint => new RenderMaterial
        {
            Name = "Pintura de auto", BaseColor = new(0.018f, 0.024f, 0.045f), Roughness = 0.08f, Metallic = 0.3f
        },
        SurfaceKind.CarPaintSilver => new RenderMaterial
        {
            Name = "Pintura plateada", BaseColor = new(0.72f, 0.73f, 0.74f), Roughness = 0.16f, Metallic = 0.85f
        },
        SurfaceKind.TintedGlass => new RenderMaterial
        {
            Name = "Vidrio polarizado", BaseColor = new(0.006f, 0.008f, 0.010f), Roughness = 0.03f, Ior = 1.5f
        },
        SurfaceKind.Rubber => new RenderMaterial
        {
            Name = "Caucho", BaseColor = new(0.025f, 0.025f, 0.025f), Roughness = 0.9f
        },
        SurfaceKind.Asphalt => new RenderMaterial
        {
            Name = "Asfalto", BaseColor = new(0.11f, 0.11f, 0.11f), Roughness = 0.95f,
            Pattern = SurfacePattern.Asphalt, PatternScale = 1f
        },
        SurfaceKind.Glass => new RenderMaterial
        {
            Name = "Vidrio", BaseColor = Vector3.One, Roughness = 0.02f, Transmission = 1,
            Ior = 1.5f, TransmissionTint = new(0.86f, 0.92f, 0.94f)
        },
        SurfaceKind.Ground => new RenderMaterial
        {
            Name = "Terreno", BaseColor = new(0.22f, 0.28f, 0.13f), Roughness = 0.97f,
            Pattern = SurfacePattern.Grass, PatternScale = 1
        },
        SurfaceKind.DoorLeaf => new RenderMaterial
        {
            Name = "Madera", BaseColor = new(0.60f, 0.39f, 0.22f), Roughness = 0.5f,
            Pattern = SurfacePattern.Planks, PatternScale = 0.4f
        },
        SurfaceKind.OpeningFrame => new RenderMaterial
        {
            Name = "Marco", BaseColor = new(0.25f, 0.27f, 0.29f), Roughness = 0.45f, Metallic = 0.35f
        },
        SurfaceKind.Foliage => new RenderMaterial
        {
            Name = "Follaje", BaseColor = new(0.085f, 0.17f, 0.045f), Roughness = 0.62f,
            Pattern = SurfacePattern.Foliage, PatternScale = 1
        },
        SurfaceKind.Bark => new RenderMaterial
        {
            Name = "Corteza", BaseColor = new(0.20f, 0.16f, 0.12f), Roughness = 0.95f,
            Pattern = SurfacePattern.Bark, PatternScale = 1
        },
        SurfaceKind.InteriorWall => ForKind(SurfaceKind.Wall) with { Name = "Pared interior" },
        SurfaceKind.LightOff => ForKind(SurfaceKind.LightEmitter) with { Name = "Difusor apagado", Emission = default },
        SurfaceKind.WallBrick => new RenderMaterial
        {
            Name = "Ladrillo", BaseColor = new(0.52f, 0.24f, 0.15f), Roughness = 0.9f, Pattern = SurfacePattern.Blocks, PatternScale = 0.4f
        },
        SurfaceKind.WallBlock => new RenderMaterial
        {
            Name = "Bloque de concreto", BaseColor = new(0.50f, 0.50f, 0.48f), Roughness = 0.92f, Pattern = SurfacePattern.Blocks, PatternScale = 1
        },
        SurfaceKind.WallConcrete => new RenderMaterial
        {
            Name = "Concreto visto", BaseColor = new(0.52f, 0.51f, 0.49f), Roughness = 0.85f, Pattern = SurfacePattern.PouredConcrete, PatternScale = 1f
        },
        SurfaceKind.WallStone => new RenderMaterial
        {
            Name = "Piedra natural", BaseColor = new(0.55f, 0.50f, 0.43f), Roughness = 0.92f, Pattern = SurfacePattern.StoneMasonry, PatternScale = 1f
        },
        SurfaceKind.WallWood => new RenderMaterial
        {
            Name = "Revestimiento de madera", BaseColor = new(0.50f, 0.34f, 0.20f), Roughness = 0.7f, Pattern = SurfacePattern.Planks, PatternScale = 1
        },
        SurfaceKind.PipeCold => new RenderMaterial
        {
            Name = "Tubería agua fría", BaseColor = new(0.16f, 0.36f, 0.72f), Roughness = 0.35f
        },
        SurfaceKind.PipeHot => new RenderMaterial
        {
            Name = "Tubería agua caliente", BaseColor = new(0.72f, 0.16f, 0.12f), Roughness = 0.35f
        },
        SurfaceKind.SolarCell => new RenderMaterial
        {
            Name = "Celda solar", BaseColor = new(0.02f, 0.04f, 0.10f), Roughness = 0.06f, Metallic = 0.2f
        },
        SurfaceKind.FireRed => new RenderMaterial
        {
            Name = "Esmalte rojo incendio", BaseColor = new(0.70f, 0.05f, 0.04f), Roughness = 0.25f
        },
        SurfaceKind.PipeStorm => new RenderMaterial
        {
            Name = "PVC pluvial", BaseColor = new(0.30f, 0.55f, 0.32f), Roughness = 0.4f
        },
        SurfaceKind.PipeDrain => new RenderMaterial
        {
            Name = "PVC sanitario", BaseColor = new(0.78f, 0.76f, 0.70f), Roughness = 0.4f
        },
        SurfaceKind.Mirror => new RenderMaterial
        {
            Name = "Espejo", BaseColor = new(0.92f, 0.93f, 0.93f), Roughness = 0.02f, Metallic = 1
        },
        SurfaceKind.WallMetal => new RenderMaterial
        {
            Name = "Panel metálico", BaseColor = new(0.70f, 0.72f, 0.74f), Roughness = 0.4f, Metallic = 0.7f
        },
        SurfaceKind.SpotEmitter => ForKind(SurfaceKind.LightEmitter) with
        {
            Name = "Lente encendida", Emission = new Vector3(1.0f, 0.86f, 0.68f) * 110f
        },
        SurfaceKind.Upholstery => new RenderMaterial
        {
            Name = "Tapiz", BaseColor = new(0.30f, 0.28f, 0.25f), Roughness = 0.95f,
            Pattern = SurfacePattern.Fabric, PatternScale = 1
        },
        SurfaceKind.Bedding => new RenderMaterial
        {
            Name = "Lino", BaseColor = new(0.80f, 0.79f, 0.76f), Roughness = 0.92f,
            Pattern = SurfacePattern.Fabric, PatternScale = 0.6f
        },
        SurfaceKind.Ceramic => new RenderMaterial
        {
            Name = "Porcelana", BaseColor = new(0.86f, 0.86f, 0.84f), Roughness = 0.07f
        },
        // Warm white, about 2700 K; the radiance of an opal diffuser in front of a 60 W-equivalent lamp.
        SurfaceKind.LightEmitter => new RenderMaterial
        {
            Name = "Difusor encendido", BaseColor = new(0.9f, 0.88f, 0.84f), Roughness = 0.6f,
            Emission = new Vector3(1.0f, 0.80f, 0.58f) * 14f
        },
        SurfaceKind.Metal => new RenderMaterial
        {
            Name = "Acero cepillado", BaseColor = new(0.62f, 0.62f, 0.63f), Roughness = 0.32f, Metallic = 1
        },
        SurfaceKind.Leaf => new RenderMaterial
        {
            Name = "Hoja", BaseColor = new(0.10f, 0.20f, 0.045f), Roughness = 0.45f, Translucency = 0.55f,
            Pattern = SurfacePattern.Leaves, PatternScale = 1
        },
        SurfaceKind.Roof => new RenderMaterial
        {
            Name = "Teja", BaseColor = new(0.50f, 0.22f, 0.12f), Roughness = 0.72f,
            Pattern = SurfacePattern.RoofTiles, PatternScale = 1
        },
        SurfaceKind.Earth => new RenderMaterial
        {
            Name = "Tierra", BaseColor = new(0.30f, 0.22f, 0.15f), Roughness = 0.97f,
            Pattern = SurfacePattern.Concrete, PatternScale = 0.6f
        },
        SurfaceKind.Rock => new RenderMaterial
        {
            Name = "Roca", BaseColor = new(0.34f, 0.32f, 0.29f), Roughness = 0.93f, Pattern = SurfacePattern.Concrete, PatternScale = 0.35f
        },
        SurfaceKind.Structure => new RenderMaterial
        {
            Name = "Estructura", BaseColor = new(0.45f, 0.42f, 0.38f), Roughness = 0.85f,
            Pattern = SurfacePattern.Concrete, PatternScale = 0.7f
        },
        _ => new RenderMaterial { Name = "Material", BaseColor = new(0.7f), Roughness = 0.8f }
    };

    /// <summary>
    /// The stock material of a category with an image texture instead of its color and pattern: the image carries the
    /// color, the category keeps its roughness, metalness and transparency.
    /// </summary>
    public static RenderMaterial Textured(SurfaceKind kind, RenderTexture texture, double size, double relief) =>
        ForKind(kind) with
        {
            Name = $"{ForKind(kind).Name} (textura)",
            BaseColor = Vector3.One,
            Pattern = SurfacePattern.None,
            Texture = texture,
            TextureSize = (float)size,
            TextureRelief = (float)relief
        };

    /// <summary>Neutral material used for imported geometry that carries no material of its own.</summary>
    public static RenderMaterial Imported { get; } =
        new() { Name = "Importado", BaseColor = new(0.72f, 0.72f, 0.70f), Roughness = 0.75f };

    /// <summary>Clear-glass material used when an import marks a surface as transparent.</summary>
    public static RenderMaterial ClearGlass { get; } = ForKind(SurfaceKind.Glass);

    /// <summary>Converts an 8-bit sRGB color (0–255) to the linear value the renderer uses.</summary>
    public static Vector3 LinearFromSrgb(byte red, byte green, byte blue) =>
        new(SrgbToLinear(red / 255f), SrgbToLinear(green / 255f), SrgbToLinear(blue / 255f));

    /// <summary>Converts one 0–1 sRGB channel to linear light.</summary>
    public static float SrgbToLinear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
}
