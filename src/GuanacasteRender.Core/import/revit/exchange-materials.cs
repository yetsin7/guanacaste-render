using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core.Import.Revit;

/// <summary>
/// Maps Revit materials onto Guanacaste render surfaces and wall constructions. Revit material names are free text
/// in any language, so the match uses Spanish and English keywords, then the material class, transparency and the
/// category of the element that owns the face.
/// </summary>
public static class ExchangeMaterials
{
    private static readonly (string[] Words, SurfaceKind Kind)[] Keywords =
    [
        (["espejo", "mirror"], SurfaceKind.Mirror),
        (["vidrio", "glass", "cristal", "glazing", "acrílico", "acrylic"], SurfaceKind.Glass),
        (["agua", "water", "piscina", "pool"], SurfaceKind.Water),
        (["hoja", "leaf", "leaves", "follaje", "foliage"], SurfaceKind.Foliage),
        (["corteza", "bark", "tronco", "trunk"], SurfaceKind.Bark),
        (["césped", "cesped", "grass", "zacate", "grama", "tierra", "soil", "terreno", "earth", "site"], SurfaceKind.Ground),
        (["asfalto", "asphalt"], SurfaceKind.Asphalt),
        (["teja", "roof tile", "shingle", "zinc", "lámina", "lamina", "techo", "roofing"], SurfaceKind.Roof),
        (["ladrillo", "brick"], SurfaceKind.WallBrick),
        (["bloque", "block", "cmu", "mampostería", "mamposteria", "masonry"], SurfaceKind.WallBlock),
        (["piedra", "stone", "mármol", "marmol", "marble", "granito", "granite", "laja"], SurfaceKind.WallStone),
        (["concreto", "concrete", "hormigón", "hormigon", "cemento", "cement"], SurfaceKind.WallConcrete),
        (["porcelana", "porcelain", "cerámica", "ceramica", "ceramic", "loza", "sanitario", "vitreous"], SurfaceKind.Ceramic),
        (["tela", "fabric", "textil", "textile", "cuero", "leather", "tapiz", "cushion", "cojín", "cojin", "alfombra", "carpet"], SurfaceKind.Upholstery),
        (["colchón", "colchon", "mattress", "sábana", "sabana", "almohada", "pillow", "linen"], SurfaceKind.Bedding),
        (["caucho", "rubber", "goma"], SurfaceKind.Rubber),
        (["acero", "steel", "metal", "aluminio", "aluminum", "aluminium", "hierro", "iron", "cromo", "chrome", "inox",
          "bronce", "bronze", "latón", "laton", "brass", "cobre", "copper"], SurfaceKind.Metal),
        (["madera", "wood", "roble", "oak", "pino", "pine", "cedro", "cedar", "teca", "teak", "laurel", "nogal", "walnut",
          "melamina", "mdf", "plywood", "contrachapado"], SurfaceKind.WallWood),
        (["luz", "light", "bombillo", "bulb", "lámpara", "lampara", "lamp", "emisor"], SurfaceKind.LightEmitter)
    ];

    /// <summary>The render surface for a material on an element of the given Revit category.</summary>
    public static SurfaceKind SurfaceFor(ExchangeMaterial? material, string elementCategory)
    {
        var text = material is null ? "" : (material.Name + " " + material.Class + " " + material.Category).ToLowerInvariant();
        if (material is { Transparency: > 0.35 }) return SurfaceKind.Glass;
        var kind = Match(text);
        if (kind is SurfaceKind.LightEmitter && elementCategory != "OST_LightingFixtures") kind = null;
        if (kind is { } matched) return Adjust(matched, elementCategory);
        return elementCategory switch
        {
            "OST_Walls" or "OST_CurtainWallPanels" or "OST_Cornices" or "OST_Reveals" => SurfaceKind.Wall,
            "OST_Floors" or "OST_StairsRuns" or "OST_StairsLandings" => SurfaceKind.Floor,
            "OST_Roofs" or "OST_RoofSoffit" or "OST_Fascia" or "OST_Gutter" => SurfaceKind.Roof,
            "OST_Ceilings" => SurfaceKind.InteriorWall,
            "OST_Doors" => SurfaceKind.DoorLeaf,
            "OST_Windows" or "OST_CurtainWallMullions" => SurfaceKind.OpeningFrame,
            "OST_PlumbingFixtures" => SurfaceKind.Ceramic,
            "OST_LightingFixtures" or "OST_LightingDevices" or "OST_Railings" or "OST_RailingTopRail"
                or "OST_RailingHandRail" or "OST_StairsRailing" => SurfaceKind.Metal,
            "OST_Planting" => SurfaceKind.Foliage,
            "OST_Topography" or "OST_Toposolid" or "OST_Site" => SurfaceKind.Ground,
            "OST_Furniture" or "OST_FurnitureSystems" or "OST_Casework" => SurfaceKind.WallWood,
            _ => SurfaceKind.Structure
        };
    }

    /// <summary>The construction whose faces best match a wall's layers, outermost finish first.</summary>
    public static WallConstruction ConstructionFor(ExchangeWall wall)
    {
        foreach (var layer in wall.Layers.Where(layer => layer.Width > 0))
        {
            var text = (layer.Material + " " + layer.Class).ToLowerInvariant();
            switch (Match(text))
            {
                case SurfaceKind.WallBrick: return WallConstruction.Brick;
                case SurfaceKind.WallBlock: return WallConstruction.Block;
                case SurfaceKind.WallConcrete when layer.Function is "Structure" && wall.Layers.Count == 1: return WallConstruction.Concrete;
                case SurfaceKind.WallStone: return WallConstruction.Stone;
                case SurfaceKind.WallWood: return WallConstruction.Wood;
                case SurfaceKind.Metal: return WallConstruction.Metal;
                case SurfaceKind.Glass: return WallConstruction.Glass;
            }
            if (text.Contains("yeso") || text.Contains("gypsum") || text.Contains("drywall") || text.Contains("gypsum"))
                return WallConstruction.Gypsum;
            if (text.Contains("repello") || text.Contains("plaster") || text.Contains("pintura") || text.Contains("paint") ||
                text.Contains("estuco") || text.Contains("stucco"))
                return WallConstruction.Plaster;
        }
        return WallConstruction.Plaster;
    }

    private static SurfaceKind? Match(string text)
    {
        foreach (var (words, kind) in Keywords)
            if (words.Any(text.Contains)) return kind;
        return null;
    }

    /// <summary>Wall finishes keep their category meaning on floors and roofs (a concrete floor is a floor).</summary>
    private static SurfaceKind Adjust(SurfaceKind kind, string category) => (kind, category) switch
    {
        (SurfaceKind.WallConcrete or SurfaceKind.WallStone or SurfaceKind.WallBlock, "OST_Floors") => SurfaceKind.Floor,
        (SurfaceKind.WallConcrete, "OST_Roofs") => SurfaceKind.Roof,
        (SurfaceKind.WallWood, "OST_Doors") => SurfaceKind.DoorLeaf,
        (SurfaceKind.WallWood, "OST_Windows") => SurfaceKind.OpeningFrame,
        _ => kind
    };
}
