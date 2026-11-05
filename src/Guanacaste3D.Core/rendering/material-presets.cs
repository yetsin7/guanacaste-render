using System.Numerics;

namespace Guanacaste3D.Core.Rendering;

/// <summary>The group a material preset is listed under.</summary>
public enum MaterialFamily { Paint, Concrete, Masonry, Stone, Wood, Floor, Metal, Glass, Fabric, Outdoor, Finish, Plastic }

/// <summary>
/// A ready-made physically based material: its colour, roughness, metalness, transparency and, when it has one, the
/// shipped photographic texture (by key) that carries its colour, with the real size of one tile and its relief.
/// </summary>
public sealed record MaterialPreset(string Id, string Name, MaterialFamily Group, RenderMaterial Material,
    string? TextureKey = null, double TextureSize = 1, double Relief = 0.2);

/// <summary>
/// The material library of the Render panel. Any surface category of a project can take one of these presets; it is
/// stored as <c>material:</c> plus the preset's identifier, like a shipped texture is stored as <c>builtin:</c>.
/// Colours are linear reflectances; roughness and index of refraction follow measured values for the real material.
/// </summary>
public static class MaterialPresets
{
    /// <summary>Prefix of a preset in a render material assignment.</summary>
    public const string Prefix = "material:";

    private static RenderMaterial Matte(string name, float r, float g, float b, float roughness = 0.9f,
        SurfacePattern pattern = SurfacePattern.None, float scale = 1) =>
        new() { Name = name, BaseColor = new(r, g, b), Roughness = roughness, Pattern = pattern, PatternScale = scale };

    private static RenderMaterial Metal(string name, float r, float g, float b, float roughness) =>
        new() { Name = name, BaseColor = new(r, g, b), Roughness = roughness, Metallic = 1 };

    private static RenderMaterial Glass(string name, Vector3 tint, float roughness = 0.02f, float ior = 1.5f) =>
        new() { Name = name, BaseColor = Vector3.One, Roughness = roughness, Transmission = 1, Ior = ior, TransmissionTint = tint };

    private static MaterialPreset Textured(string id, string name, MaterialFamily group, string key, double size, float roughness,
        double relief = 0.2, float metallic = 0) =>
        new(id, name, group, new RenderMaterial { Name = name, BaseColor = Vector3.One, Roughness = roughness, Metallic = metallic },
            key, size, relief);

    public static IReadOnlyList<MaterialPreset> All { get; } =
    (new MaterialPreset[]
    {
        // Construction-ready PBR defaults; all use the existing procedural shading engine.
        new("nicaragua-block-grey", "Bloque de concreto 20×20×40", MaterialFamily.Masonry,
            Matte("Bloque de concreto", .42f, .42f, .40f, .95f, SurfacePattern.Blocks, 1)),
        new("nicaragua-brick-clay", "Ladrillo de barro cocido", MaterialFamily.Masonry,
            Matte("Ladrillo de barro cocido", .51f, .19f, .095f, .93f, SurfacePattern.Blocks, .5f)),
        new("nicaragua-repello", "Repello de mortero cemento-arena", MaterialFamily.Finish,
            Matte("Repello de mortero", .59f, .57f, .52f, .97f, SurfacePattern.Concrete, 1.7f)),
        new("nicaragua-fino", "Fino de pared blanco", MaterialFamily.Finish,
            Matte("Fino de pared", .83f, .81f, .76f, .91f, SurfacePattern.Concrete, 3f)),
        new("nicaragua-ceramic-terracotta", "Cerámica terracota mate", MaterialFamily.Floor,
            Matte("Cerámica terracota", .49f, .21f, .12f, .78f, SurfacePattern.Tiles, .45f)),
        new("nicaragua-porcelain-cream", "Porcelanato crema brillante", MaterialFamily.Floor,
            Matte("Porcelanato crema", .72f, .66f, .54f, .14f, SurfacePattern.Tiles, .65f)),
        new("nicaragua-roof-tiles", "Teja de barro rojiza", MaterialFamily.Outdoor,
            Matte("Teja de barro", .48f, .19f, .11f, .87f, SurfacePattern.RoofTiles, 1)),
        new("nicaragua-stone-river", "Piedra de río natural", MaterialFamily.Stone,
            Matte("Piedra de río", .38f, .36f, .32f, .84f, SurfacePattern.StoneMasonry, 1.2f)),
        // Retaining-wall material set. These are appearance presets, not structural design details.
        // Stone uses the optional render-only microgeometry for real contact shadows on WallStone faces.
        new("retaining-stone-grey", "Piedra gris cortada · muro de contención", MaterialFamily.Stone,
            Matte("Piedra gris cortada", .47f, .47f, .44f, .95f, SurfacePattern.StoneMasonry, .85f)),
        new("retaining-stone-sand", "Piedra arenisca labrada · muro de contención", MaterialFamily.Stone,
            Matte("Piedra arenisca labrada", .58f, .52f, .43f, .94f, SurfacePattern.StoneMasonry, 1.05f)),
        new("retaining-footing", "Concreto expuesto de zapata", MaterialFamily.Concrete,
            Matte("Concreto de zapata", .49f, .48f, .45f, .94f, SurfacePattern.PouredConcrete, 1.2f)),
        Textured("retaining-drainage-gravel", "Grava drenante 20–40 mm", MaterialFamily.Outdoor,
            "gravel", .8, .98f, .65),
        Textured("retaining-backfill-earth", "Tierra de relleno compactado", MaterialFamily.Outdoor,
            "ambientcg-ground108", 1.2, .98f, .35),
        new("nicaragua-paving", "Adoquín gris exterior", MaterialFamily.Outdoor,
            Matte("Adoquín gris", .35f, .36f, .34f, .92f, SurfacePattern.Blocks, 1)),
        new("nicaragua-polished-cement", "Piso de concreto afinado", MaterialFamily.Concrete,
            Matte("Piso de concreto afinado", .51f, .50f, .46f, .33f, SurfacePattern.PouredConcrete, 1.4f)),
        new("nicaragua-pvc-white", "PVC blanco satinado", MaterialFamily.Plastic,
            Matte("PVC blanco", .72f, .72f, .69f, .32f)),
        new("nicaragua-galvanized", "Acero galvanizado", MaterialFamily.Metal,
            Metal("Acero galvanizado", .56f, .59f, .60f, .42f)),
        new("nicaragua-gypsum", "Panel gypsum blanco", MaterialFamily.Finish,
            Matte("Panel gypsum", .82f, .81f, .78f, .93f)),
        new("nicaragua-plywood", "Plywood natural", MaterialFamily.Wood,
            Matte("Plywood", .46f, .31f, .17f, .62f, SurfacePattern.Planks, .5f)),
        new("nicaragua-dark-aluminium", "Aluminio negro mate", MaterialFamily.Metal,
            Metal("Aluminio negro mate", .04f, .044f, .045f, .53f)),

        // Paint and plaster.
        new("paint-white", "Pintura blanca mate", MaterialFamily.Paint, Matte("Pintura blanca mate", 0.83f, 0.82f, 0.79f, 0.92f, SurfacePattern.Concrete, 2)),
        Textured("plaster-white", "Estuco blanco texturizado", MaterialFamily.Paint, "ambientcg-plaster001", 1.0, 0.9f, 0.15),
        new("paint-warm-white", "Pintura blanco hueso", MaterialFamily.Paint, Matte("Pintura blanco hueso", 0.80f, 0.76f, 0.66f, 0.9f)),
        new("paint-light-grey", "Pintura gris claro", MaterialFamily.Paint, Matte("Pintura gris claro", 0.55f, 0.55f, 0.53f, 0.9f)),
        new("paint-sand", "Pintura arena", MaterialFamily.Paint, Matte("Pintura arena", 0.62f, 0.52f, 0.38f, 0.9f)),
        new("paint-terracotta", "Pintura terracota", MaterialFamily.Paint, Matte("Pintura terracota", 0.48f, 0.20f, 0.11f, 0.88f)),
        new("paint-sage", "Pintura verde salvia", MaterialFamily.Paint, Matte("Pintura verde salvia", 0.30f, 0.36f, 0.27f, 0.9f)),
        new("paint-navy", "Pintura azul profundo", MaterialFamily.Paint, Matte("Pintura azul profundo", 0.04f, 0.07f, 0.16f, 0.85f)),
        new("paint-charcoal", "Pintura carbón mate", MaterialFamily.Paint, Matte("Pintura carbón mate", 0.035f, 0.035f, 0.038f, 0.85f)),
        new("lacquer-black", "Laca negra brillante", MaterialFamily.Paint, Matte("Laca negra brillante", 0.02f, 0.02f, 0.02f, 0.08f)),
        new("lacquer-white", "Laca blanca brillante", MaterialFamily.Paint, Matte("Laca blanca brillante", 0.82f, 0.82f, 0.80f, 0.1f)),

        // Concrete.
        Textured("concrete-smooth", "Concreto visto liso", MaterialFamily.Concrete, "ambientcg-concrete046", 2.4, 0.82f, 0.12),
        Textured("concrete-board", "Concreto visto con tablilla", MaterialFamily.Concrete, "ambientcg-concrete034", 1.1, 0.85f, 0.18),
        Textured("concrete-polished", "Concreto pulido", MaterialFamily.Concrete, "ambientcg-concrete046", 2.4, 0.3f, 0.05),
        new("microcement", "Microcemento gris", MaterialFamily.Concrete, Matte("Microcemento gris", 0.40f, 0.39f, 0.37f, 0.55f, SurfacePattern.Concrete, 2.5f)),
        new("concrete-dark", "Concreto oscuro", MaterialFamily.Concrete, Matte("Concreto oscuro", 0.17f, 0.17f, 0.17f, 0.85f, SurfacePattern.Concrete, 1.4f)),

        // Masonry.
        Textured("brick-red", "Ladrillo rojo", MaterialFamily.Masonry, "ambientcg-bricks105", 2.4, 0.9f, 0.35),
        Textured("brick-old", "Ladrillo envejecido", MaterialFamily.Masonry, "ambientcg-bricks113a", 1.0, 0.9f, 0.35),
        new("block-grey", "Bloque de concreto gris", MaterialFamily.Masonry, Matte("Bloque de concreto gris", 0.42f, 0.42f, 0.40f, 0.92f, SurfacePattern.Blocks, 1)),
        new("block-white", "Bloque pintado blanco", MaterialFamily.Masonry, Matte("Bloque pintado blanco", 0.80f, 0.79f, 0.76f, 0.9f, SurfacePattern.Blocks, 1)),

        // Stone.
        Textured("marble-white", "Mármol blanco pulido", MaterialFamily.Stone, "ambientcg-marble012", 1.0, 0.12f, 0.03),
        Textured("marble-honed", "Mármol blanco apomazado", MaterialFamily.Stone, "ambientcg-marble012", 1.0, 0.45f, 0.05),
        new("granite-black", "Granito negro pulido", MaterialFamily.Stone, Matte("Granito negro pulido", 0.03f, 0.03f, 0.032f, 0.1f, SurfacePattern.Concrete, 0.3f)),
        new("travertine", "Travertino", MaterialFamily.Stone, Matte("Travertino", 0.62f, 0.53f, 0.40f, 0.6f, SurfacePattern.Concrete, 0.8f)),
        new("slate", "Pizarra", MaterialFamily.Stone, Matte("Pizarra", 0.09f, 0.10f, 0.11f, 0.7f, SurfacePattern.Blocks, 0.6f)),
        new("stone-ledge", "Piedra laja", MaterialFamily.Stone, Matte("Piedra laja", 0.40f, 0.36f, 0.30f, 0.9f, SurfacePattern.Blocks, 1.5f)),
        // Photographic rough stone from the bundled image, ideal for garden walls and estate exteriors.
        Textured("stone-natural-wall", "Mampostería de piedra natural", MaterialFamily.Stone, "stone-paving", 1.8, 0.94f, 0.65),
        Textured("stone-beige-facade", "Piedra beige para fachada", MaterialFamily.Stone, "stone-paving", 2.1, 0.88f, 0.45),

        // Wood.
        Textured("oak-light", "Roble claro", MaterialFamily.Wood, "ambientcg-wood096", 0.5, 0.55f, 0.15),
        Textured("walnut", "Nogal", MaterialFamily.Wood, "ambientcg-wood092", 0.8, 0.45f, 0.15),
        Textured("teak", "Teca", MaterialFamily.Wood, "ambientcg-wood051", 0.8, 0.6f, 0.15),
        Textured("wood-varnished", "Madera barnizada brillante", MaterialFamily.Wood, "ambientcg-wood096", 0.5, 0.18f, 0.08),
        Textured("wood-cladding", "Revestimiento de listones", MaterialFamily.Wood, "ambientcg-wood051", 0.8, 0.75f, 0.3),
        new("wood-burnt", "Madera quemada (shou sugi ban)", MaterialFamily.Wood, Matte("Madera quemada", 0.025f, 0.022f, 0.02f, 0.75f, SurfacePattern.Planks, 0.5f)),
        new("wood-white", "Madera pintada blanca", MaterialFamily.Wood, Matte("Madera pintada blanca", 0.80f, 0.79f, 0.75f, 0.6f, SurfacePattern.Planks, 0.4f)),

        // Floors and tiles.
        Textured("porcelain-grey", "Porcelanato gris gran formato", MaterialFamily.Floor, "ambientcg-tiles141", 2.0, 0.3f, 0.1),
        Textured("hydraulic-tile", "Baldosa hidráulica", MaterialFamily.Floor, "ambientcg-tiles107", 1.0, 0.45f, 0.15),
        new("porcelain-white", "Porcelanato blanco brillante", MaterialFamily.Floor, Matte("Porcelanato blanco brillante", 0.78f, 0.78f, 0.76f, 0.08f, SurfacePattern.Tiles, 0.6f)),
        new("terrazzo", "Terrazo", MaterialFamily.Floor, Matte("Terrazo", 0.62f, 0.60f, 0.56f, 0.25f, SurfacePattern.Concrete, 0.25f)),
        new("ceramic-beige", "Cerámica beige", MaterialFamily.Floor, Matte("Cerámica beige", 0.58f, 0.50f, 0.40f, 0.3f, SurfacePattern.Tiles, 0.4f)),

        // Metals.
        new("steel-brushed", "Acero inoxidable cepillado", MaterialFamily.Metal, Metal("Acero inoxidable cepillado", 0.62f, 0.62f, 0.63f, 0.3f) with { Pattern = SurfacePattern.Brushed, PatternScale = 1 }),
        new("chrome", "Cromo pulido", MaterialFamily.Metal, Metal("Cromo pulido", 0.96f, 0.96f, 0.97f, 0.015f)),
        new("aluminium-black", "Aluminio anodizado negro", MaterialFamily.Metal, Metal("Aluminio anodizado negro", 0.035f, 0.035f, 0.04f, 0.35f)),
        new("aluminium-natural", "Aluminio natural", MaterialFamily.Metal, Metal("Aluminio natural", 0.91f, 0.92f, 0.92f, 0.4f)),
        new("copper", "Cobre", MaterialFamily.Metal, Metal("Cobre", 0.95f, 0.64f, 0.54f, 0.22f)),
        new("brass", "Latón", MaterialFamily.Metal, Metal("Latón", 0.80f, 0.68f, 0.36f, 0.34f)),
        new("bronze", "Bronce envejecido", MaterialFamily.Metal, Metal("Bronce envejecido", 0.45f, 0.30f, 0.17f, 0.45f)),
        new("corten", "Acero corten", MaterialFamily.Metal, Matte("Acero corten", 0.30f, 0.10f, 0.04f, 0.8f, SurfacePattern.Concrete, 0.6f)),
        new("zinc", "Zinc", MaterialFamily.Metal, Metal("Zinc", 0.50f, 0.52f, 0.54f, 0.5f) with { Pattern = SurfacePattern.Concrete, PatternScale = 0.45f }),

        // Glass.
        new("glass-clear", "Vidrio claro", MaterialFamily.Glass, Glass("Vidrio claro", new(0.86f, 0.92f, 0.94f))),
        new("glass-low-iron", "Vidrio extraclaro", MaterialFamily.Glass, Glass("Vidrio extraclaro", new(0.96f, 0.98f, 0.98f))),
        new("glass-frosted", "Vidrio esmerilado", MaterialFamily.Glass, Glass("Vidrio esmerilado", new(0.9f, 0.93f, 0.94f), 0.35f)),
        new("glass-smoked", "Vidrio ahumado", MaterialFamily.Glass, Glass("Vidrio ahumado", new(0.35f, 0.36f, 0.37f))),
        new("glass-green", "Vidrio verde", MaterialFamily.Glass, Glass("Vidrio verde", new(0.62f, 0.82f, 0.70f))),
        new("glass-reflective", "Vidrio reflectivo", MaterialFamily.Glass, Glass("Vidrio reflectivo", new(0.30f, 0.38f, 0.42f), 0.01f, 2.2f)),
        new("mirror", "Espejo", MaterialFamily.Glass, Metal("Espejo", 0.92f, 0.93f, 0.93f, 0.02f)),

        // Fabric and leather.
        new("linen-white", "Lino blanco", MaterialFamily.Fabric, Matte("Lino blanco", 0.78f, 0.76f, 0.71f, 0.95f, SurfacePattern.Fabric, 0.6f)),
        Textured("fabric-grey", "Tela gris", MaterialFamily.Fabric, "ambientcg-fabric085", 0.3, 0.95f, 0.1),
        new("velvet-green", "Terciopelo verde", MaterialFamily.Fabric, Matte("Terciopelo verde", 0.05f, 0.14f, 0.08f, 0.85f, SurfacePattern.Fabric, 0.4f)),
        new("boucle", "Bouclé crema", MaterialFamily.Fabric, Matte("Bouclé crema", 0.72f, 0.67f, 0.58f, 0.98f, SurfacePattern.Fabric, 0.3f)),
        new("leather-brown", "Cuero café", MaterialFamily.Fabric, Matte("Cuero café", 0.18f, 0.08f, 0.035f, 0.45f, SurfacePattern.Fabric, 0.2f)),
        new("leather-black", "Cuero negro", MaterialFamily.Fabric, Matte("Cuero negro", 0.02f, 0.02f, 0.02f, 0.4f, SurfacePattern.Fabric, 0.2f)),

        // Outdoor.
        new("lawn", "Césped", MaterialFamily.Outdoor, Matte("Césped", 0.20f, 0.30f, 0.10f, 0.97f, SurfacePattern.Grass, 1)),
        new("lawn-dry", "Césped seco", MaterialFamily.Outdoor, Matte("Césped seco", 0.36f, 0.33f, 0.16f, 0.97f, SurfacePattern.Grass, 1)),
        Textured("soil", "Tierra", MaterialFamily.Outdoor, "ambientcg-ground108", 1.5, 0.97f, 0.25),
        new("gravel", "Grava", MaterialFamily.Outdoor, Matte("Grava", 0.42f, 0.40f, 0.36f, 0.95f, SurfacePattern.Concrete, 0.12f)),
        new("asphalt", "Asfalto", MaterialFamily.Outdoor, Matte("Asfalto", 0.09f, 0.09f, 0.095f, 0.95f, SurfacePattern.Concrete, 0.4f)),
        new("pavers", "Adoquín", MaterialFamily.Outdoor, Matte("Adoquín", 0.38f, 0.33f, 0.28f, 0.88f, SurfacePattern.Blocks, 0.35f)),
        new("pool-water", "Agua de piscina", MaterialFamily.Outdoor, new RenderMaterial
        {
            Name = "Agua de piscina", BaseColor = new(0.02f, 0.16f, 0.20f), Roughness = 0.01f, Ior = 1.33f
        }),
        new("dark-water", "Agua oscura", MaterialFamily.Outdoor, MaterialLibrary.ForKind(Geometry.SurfaceKind.Water)),

        // Other finishes.
        new("ceramic-sanitary", "Porcelana sanitaria", MaterialFamily.Finish, MaterialLibrary.ForKind(Geometry.SurfaceKind.Ceramic)),
        new("plastic-white", "Plástico blanco", MaterialFamily.Plastic, Matte("Plástico blanco", 0.80f, 0.80f, 0.78f, 0.3f)),
        new("rubber", "Caucho", MaterialFamily.Plastic, MaterialLibrary.ForKind(Geometry.SurfaceKind.Rubber)),
        new("car-paint-red", "Pintura de auto roja", MaterialFamily.Finish, new RenderMaterial
        {
            Name = "Pintura de auto roja", BaseColor = new(0.42f, 0.02f, 0.02f), Roughness = 0.08f, Metallic = 0.3f
        }),
        new("car-paint-white", "Pintura de auto blanca", MaterialFamily.Finish, new RenderMaterial
        {
            Name = "Pintura de auto blanca", BaseColor = new(0.80f, 0.80f, 0.79f), Roughness = 0.1f, Metallic = 0.1f
        })
    }).Concat(ReferenceCatalog()).Concat(ExpandedCatalog()).ToArray();

    /// <summary>
    /// Materials that a visualiser reaches for first, as in the reference sheets of Lumion and V-Ray: the metals one by one (steel,
    /// iron, brushed, galvanized, lead, dirty copper, grill, pearl, rust under paint), plastics and rubbers, hard natural
    /// surfaces and living ones. Each is told apart by what the ball shows: gloss, metal, and a pattern where the real thing has one.
    /// </summary>
    private static IEnumerable<MaterialPreset> ReferenceCatalog()
    {
        MaterialPreset Patterned(string id, string name, MaterialFamily group, float r, float g, float b, float roughness, float metallic,
            SurfacePattern pattern, float scale = 1) =>
            new(id, name, group, new RenderMaterial { Name = name, BaseColor = new(r, g, b), Roughness = roughness, Metallic = metallic, Pattern = pattern, PatternScale = scale });

        // Metals.
        yield return Patterned("metal-steel", "Acero", MaterialFamily.Metal, .50f, .53f, .58f, .24f, 1, SurfacePattern.None);
        yield return Patterned("metal-iron", "Hierro fundido", MaterialFamily.Metal, .13f, .13f, .14f, .62f, 1, SurfacePattern.Concrete, .05f);
        yield return Patterned("metal-brushed", "Metal cepillado", MaterialFamily.Metal, .66f, .66f, .67f, .32f, 1, SurfacePattern.Brushed);
        yield return Patterned("metal-aluminium-brushed", "Aluminio cepillado", MaterialFamily.Metal, .88f, .89f, .9f, .3f, 1, SurfacePattern.Brushed, 1.5f);
        yield return Patterned("metal-galvanized-spangle", "Galvanizado", MaterialFamily.Metal, .50f, .53f, .56f, .42f, 1, SurfacePattern.Concrete, .15f);
        yield return Patterned("metal-lead", "Plomo", MaterialFamily.Metal, .30f, .32f, .38f, .58f, 1, SurfacePattern.Concrete, .1f);
        yield return Patterned("metal-copper-dirty", "Cobre sucio", MaterialFamily.Metal, .85f, .5f, .4f, .38f, 1, SurfacePattern.Patina);
        yield return Patterned("metal-grill", "Rejilla metálica", MaterialFamily.Metal, .60f, .61f, .62f, .28f, 1, SurfacePattern.Perforated);
        yield return Patterned("metal-pearl", "Perla decorativa", MaterialFamily.Metal, .96f, .86f, .88f, .06f, .55f, SurfacePattern.None);
        yield return Patterned("metal-rust-paint", "Metal oxidado con pintura", MaterialFamily.Metal, .07f, .26f, .55f, .62f, .6f, SurfacePattern.RustPaint);
        yield return Patterned("metal-gold", "Oro", MaterialFamily.Metal, 1.0f, .72f, .20f, .09f, 1, SurfacePattern.None);

        // Plastics and rubbers.
        yield return Patterned("plastic-carbon-fiber", "Fibra de carbono", MaterialFamily.Plastic, .035f, .035f, .04f, .12f, 0, SurfacePattern.CarbonFiber);
        yield return Patterned("plastic-glossy-black", "Plástico brillante", MaterialFamily.Plastic, .02f, .02f, .022f, .06f, 0, SurfacePattern.None);
        yield return Patterned("plastic-rough", "Plástico rugoso", MaterialFamily.Plastic, .22f, .30f, .32f, .7f, 0, SurfacePattern.Concrete, .08f);
        yield return Patterned("plastic-red", "Plástico rojo", MaterialFamily.Plastic, .55f, .04f, .04f, .22f, 0, SurfacePattern.None);
        yield return Patterned("plastic-blue", "Plástico azul", MaterialFamily.Plastic, .04f, .14f, .5f, .22f, 0, SurfacePattern.None);
        yield return Patterned("rubber-black", "Caucho negro", MaterialFamily.Plastic, .03f, .03f, .032f, .8f, 0, SurfacePattern.Concrete, .08f);
        yield return Patterned("rubber-worn", "Caucho desgastado", MaterialFamily.Plastic, .05f, .05f, .052f, .88f, 0, SurfacePattern.WornRubber);
        yield return Patterned("tarp", "Lona", MaterialFamily.Plastic, .28f, .34f, .36f, .65f, 0, SurfacePattern.Fabric, .8f);

        // Hard natural and mineral surfaces.
        yield return Patterned("asphalt-worn", "Asfalto gastado", MaterialFamily.Stone, .06f, .06f, .065f, .95f, 0, SurfacePattern.Asphalt, 1.5f);
        yield return Patterned("concrete-weathered", "Concreto envejecido", MaterialFamily.Concrete, .36f, .36f, .35f, .88f, 0, SurfacePattern.PouredConcrete, .9f);
        yield return Patterned("cobblestone", "Adoquín de piedra", MaterialFamily.Stone, .09f, .09f, .10f, .8f, 0, SurfacePattern.StoneMasonry, .5f);
        yield return Patterned("sand", "Arena", MaterialFamily.Outdoor, .55f, .46f, .30f, .97f, 0, SurfacePattern.Concrete, .05f);

        // Living surfaces.
        yield return Patterned("bark-pine", "Corteza de pino", MaterialFamily.Outdoor, .16f, .10f, .065f, .92f, 0, SurfacePattern.Bark, 2);
        yield return Patterned("moss", "Musgo", MaterialFamily.Outdoor, .10f, .22f, .05f, .97f, 0, SurfacePattern.Grass, .4f);
        // Lawns and grasses: the pattern's scale is the size of a blade, so a fine bermuda and a broad St. Augustine differ close up.
        yield return Patterned("grass-bermuda", "Grama bermuda", MaterialFamily.Outdoor, .17f, .30f, .08f, .97f, 0, SurfacePattern.Grass, .6f);
        yield return Patterned("grass-st-augustine", "Grama San Agustín", MaterialFamily.Outdoor, .10f, .24f, .07f, .97f, 0, SurfacePattern.Grass, 1.6f);
        yield return Patterned("grass-meadow", "Hierba de prado", MaterialFamily.Outdoor, .22f, .30f, .09f, .97f, 0, SurfacePattern.Grass, 1.1f);
        yield return Patterned("grass-dry", "Pasto seco de verano", MaterialFamily.Outdoor, .46f, .38f, .17f, .97f, 0, SurfacePattern.Grass, 1.2f);
        yield return Patterned("grass-clover", "Trébol y hierba baja", MaterialFamily.Outdoor, .14f, .33f, .12f, .97f, 0, SurfacePattern.Grass, .9f);
        yield return Patterned("snow", "Nieve", MaterialFamily.Outdoor, .85f, .88f, .92f, .6f, 0, SurfacePattern.Concrete, .06f);
    }

    /// <summary>Additional physically based colour/finish variations without external texture dependencies.</summary>
    private static IEnumerable<MaterialPreset> ExpandedCatalog()
    {
        var colors = new (string Id, string Label, float R, float G, float B)[]
        {
            ("ivory", "Marfil", .78f, .73f, .61f),
            ("sand", "Arena", .60f, .49f, .35f),
            ("clay", "Arcilla", .48f, .25f, .16f),
            ("sage", "Salvia", .29f, .38f, .27f),
            ("forest", "Bosque", .08f, .22f, .12f),
            ("ocean", "Océano", .06f, .22f, .34f),
            ("slate", "Pizarra", .20f, .23f, .26f),
            ("charcoal", "Carbón", .055f, .055f, .06f),
            ("rose", "Rosa empolvado", .58f, .32f, .33f),
            ("white", "Blanco", .82f, .82f, .79f),
            ("grey", "Gris", .40f, .41f, .42f),
            ("black", "Negro", .025f, .025f, .027f)
        };
        foreach (var color in colors)
        {
            yield return new MaterialPreset("paint-satin-" + color.Id, "Pintura satinada " + color.Label,
                MaterialFamily.Paint, Matte("Pintura satinada " + color.Label, color.R, color.G, color.B, .32f));
            yield return new MaterialPreset("paint-matte-" + color.Id, "Pintura mate " + color.Label,
                MaterialFamily.Paint, Matte("Pintura mate " + color.Label, color.R, color.G, color.B, .92f));
            yield return new MaterialPreset("stucco-" + color.Id, "Estuco " + color.Label,
                MaterialFamily.Paint, Matte("Estuco " + color.Label, color.R, color.G, color.B, .88f,
                    SurfacePattern.Concrete, 2.2f));
            yield return new MaterialPreset("ceramic-" + color.Id, "Cerámica " + color.Label,
                MaterialFamily.Floor, Matte("Cerámica " + color.Label, color.R, color.G, color.B, .18f,
                    SurfacePattern.Tiles, .65f));
            yield return new MaterialPreset("fabric-woven-" + color.Id, "Tela " + color.Label,
                MaterialFamily.Fabric, Matte("Tela " + color.Label, color.R, color.G, color.B, .95f,
                    SurfacePattern.Fabric, .5f));
        }
    }

    /// <summary>The preset stored in an assignment path, or null when the path is not a preset.</summary>
    public static MaterialPreset? FromPath(string? path) =>
        path is not null && path.StartsWith(Prefix, StringComparison.Ordinal)
            ? All.FirstOrDefault(preset => preset.Id == path[Prefix.Length..])
            : null;

    /// <summary>
    /// The render material of a preset; for a textured preset <paramref name="texture"/> supplies the image (null keeps
    /// the plain colour, for instance while the image is missing).
    /// </summary>
    public static RenderMaterial Build(MaterialPreset preset, RenderTexture? texture) => texture is null
        ? preset.TextureKey is null ? preset.Material : preset.Material with { BaseColor = new Vector3(0.5f) }
        : preset.Material with
        {
            Pattern = SurfacePattern.None, Texture = texture, TextureSize = (float)preset.TextureSize, TextureRelief = (float)preset.Relief
        };
}
