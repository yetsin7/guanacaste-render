using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Core;

/// <summary>Builds lightweight, recognizable solids for the default residential component families.</summary>
public static partial class ResidentialComponentGeometry
{
    /// <summary>Returns the coordinated plan and 3D solids for one residential component instance, turned to its rotation.</summary>
    public static IEnumerable<ElementPart> Parts(StructuralElement element)
    {
        var parts = Unrotated(element);
        if (Math.Abs(element.Rotation) < 1e-12) return parts;
        var (cos, sin) = (Math.Cos(element.Rotation), Math.Sin(element.Rotation));
        var origin = element.Start;
        Point2 Turn(Point2 point) => new(origin.X + (point.X - origin.X) * cos - (point.Y - origin.Y) * sin,
            origin.Y + (point.X - origin.X) * sin + (point.Y - origin.Y) * cos);
        return parts.Select(part => part with { Footprint = part.Footprint.Select(Turn).ToArray() });
    }

    /// <summary>The solids as authored, front toward -Y, before the instance's rotation.</summary>
    private static IEnumerable<ElementPart> Unrotated(StructuralElement element) => element.ComponentShape switch
    {
        ResidentialComponentShape.Table => Table(element),
        ResidentialComponentShape.Chair => Chair(element),
        ResidentialComponentShape.Sofa => Sofa(element),
        ResidentialComponentShape.Bed => Bed(element),
        ResidentialComponentShape.Plant => Plant(element),
        ResidentialComponentShape.Cabinet or ResidentialComponentShape.WallCabinet => Cabinet(element),
        ResidentialComponentShape.Refrigerator => Refrigerator(element),
        ResidentialComponentShape.Range => Range(element),
        ResidentialComponentShape.Dishwasher or ResidentialComponentShape.WasherDryer or ResidentialComponentShape.ClothesDryer or
            ResidentialComponentShape.Microwave => Appliance(element),
        ResidentialComponentShape.Toilet => Toilet(element),
        ResidentialComponentShape.Vanity => Vanity(element),
        ResidentialComponentShape.Bathtub => Bathtub(element),
        ResidentialComponentShape.Shower => Shower(element),
        ResidentialComponentShape.Desk => Desk(element),
        ResidentialComponentShape.Dresser => Dresser(element),
        ResidentialComponentShape.Bookshelf => Bookshelf(element),
        ResidentialComponentShape.Television => Television(element),
        ResidentialComponentShape.Rug => [Block(element, 0, 0, Width(element), Depth(element), 0, element.Height, SurfaceKind.Upholstery)],
        ResidentialComponentShape.FloorLamp => FloorLamp(element),
        ResidentialComponentShape.CeilingLight => CeilingLight(element),
        ResidentialComponentShape.Barbecue => Barbecue(element),
        ResidentialComponentShape.RoundTable => RoundTable(element),
        ResidentialComponentShape.Sectional => Sectional(element),
        ResidentialComponentShape.OfficeChair => OfficeChair(element),
        ResidentialComponentShape.BarStool => BarStool(element),
        ResidentialComponentShape.Ottoman => Ottoman(element),
        ResidentialComponentShape.BunkBed => BunkBed(element),
        ResidentialComponentShape.Crib => Crib(element),
        ResidentialComponentShape.KitchenSink => KitchenSink(element),
        ResidentialComponentShape.RangeHood => RangeHood(element),
        ResidentialComponentShape.PedestalSink => PedestalSink(element),
        ResidentialComponentShape.Bidet => Bidet(element),
        ResidentialComponentShape.Urinal => Urinal(element),
        ResidentialComponentShape.Mirror => Mirror(element),
        ResidentialComponentShape.WaterHeater => WaterHeater(element),
        ResidentialComponentShape.Hammock => Hammock(element),
        ResidentialComponentShape.SunLounger => SunLounger(element),
        ResidentialComponentShape.Parasol => Parasol(element),
        ResidentialComponentShape.Car => Car(element, SurfaceKind.CarPaint),
        ResidentialComponentShape.SilverCar => Car(element, SurfaceKind.CarPaintSilver),
        _ => [Block(element, 0, 0, Width(element), Depth(element), 0, element.Height)]
    };

    /// <summary>Builds a top and four legs, scaling all members to the family dimensions.</summary>
    private static IEnumerable<ElementPart> Table(StructuralElement element)
    {
        var top = Math.Clamp(element.Height * 0.08, 0.035, 0.08);
        var leg = Math.Clamp(Math.Min(Width(element), Depth(element)) * 0.08, 0.035, 0.09);
        yield return Block(element, 0, 0, Width(element), Depth(element), element.Height - top, element.Height,
            SurfaceKind.DoorLeaf);
        foreach (var (x, y) in Corners(element, leg * 1.7))
            yield return Block(element, x, y, leg, leg, 0, element.Height - top, SurfaceKind.DoorLeaf);
    }

    /// <summary>Builds a chair with a seat, four legs, and a back panel.</summary>
    private static IEnumerable<ElementPart> Chair(StructuralElement element)
    {
        var seatTop = Math.Min(0.48, element.Height * 0.55);
        var member = Math.Clamp(Math.Min(Width(element), Depth(element)) * 0.09, 0.025, 0.05);
        yield return Block(element, 0, 0, Width(element), Depth(element), seatTop - 0.07, seatTop, SurfaceKind.DoorLeaf);
        foreach (var (x, y) in Corners(element, member * 1.5))
            yield return Block(element, x, y, member, member, 0, seatTop - 0.07, SurfaceKind.DoorLeaf);
        yield return Block(element, 0, Depth(element) / 2 - member, Width(element), member * 1.4,
            seatTop, element.Height, SurfaceKind.DoorLeaf);
    }

    /// <summary>Builds upholstered seating with a base, back, arms, and separate seat cushions.</summary>
    private static IEnumerable<ElementPart> Sofa(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        var arm = Math.Clamp(width * 0.08, 0.09, 0.18);
        const SurfaceKind fabric = SurfaceKind.Upholstery;
        yield return Soft(element, 0, 0, width, depth, 0.12, 0.38, 0.04, fabric);
        yield return Soft(element, 0, depth * 0.34, width, depth * 0.28, 0.32, element.Height, 0.06, fabric);
        foreach (var side in new[] { -1, 1 })
            yield return Soft(element, side * (width / 2 - arm / 2), 0, arm, depth, 0.20, element.Height * 0.72, 0.04, fabric);
        // Short dark legs lift the base, as on most contemporary sofas.
        foreach (var (x, y) in Corners(element, 0.08))
            yield return Block(element, x, y, 0.04, 0.04, 0, 0.12, SurfaceKind.DoorLeaf);
        var cushionCount = Math.Clamp((int)Math.Round(width / 0.70), 1, 3);
        var cushionWidth = (width - 2 * arm - 0.04 * (cushionCount - 1)) / cushionCount;
        for (var index = 0; index < cushionCount; index++)
        {
            var x = -width / 2 + arm + cushionWidth / 2 + index * (cushionWidth + 0.04);
            yield return Soft(element, x, -depth * 0.08, cushionWidth, depth * 0.58, 0.38, 0.52, 0.05, fabric);
            // Back cushion leaning on the back.
            yield return Soft(element, x, depth * 0.17, cushionWidth, 0.16, 0.50, element.Height - 0.04, 0.05, fabric);
        }
    }

    /// <summary>Builds a bed frame, mattress, headboard, and two pillows when the bed is wide enough.</summary>
    private static IEnumerable<ElementPart> Bed(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        yield return Block(element, 0, 0, width, depth, 0.08, 0.25, SurfaceKind.DoorLeaf);
        yield return Soft(element, 0, 0, width * 0.96, depth * 0.91, 0.25, 0.48, 0.06, SurfaceKind.Bedding);
        // A duvet over the lower two thirds, hanging a little past the mattress.
        yield return Soft(element, 0, -depth * 0.12, width * 1.0, depth * 0.66, 0.30, 0.52, 0.05, SurfaceKind.Upholstery);
        yield return Block(element, 0, depth / 2 - 0.055, width, 0.11, 0.08, element.Height, SurfaceKind.DoorLeaf);
        var pillowCount = width >= 1.30 ? 2 : 1;
        for (var index = 0; index < pillowCount; index++)
            yield return Soft(element, pillowCount == 1 ? 0 : (index == 0 ? -1 : 1) * width * 0.24,
                depth * 0.30, width * (pillowCount == 1 ? 0.55 : 0.38), depth * 0.20, 0.48, 0.61, 0.06, SurfaceKind.Bedding);
    }

    /// <summary>Builds an octagonal pot, a trunk, and three offset foliage masses.</summary>
    private static IEnumerable<ElementPart> Plant(StructuralElement element)
    {
        var diameter = Math.Min(Width(element), Depth(element));
        var potTop = Math.Min(element.Height * 0.28, diameter * 0.75);
        yield return RoundBlock(element, 0, 0, diameter * 0.58, 0, potTop, SurfaceKind.DoorLeaf);
        yield return RoundBlock(element, 0, 0, Math.Max(0.025, diameter * 0.10), potTop * 0.7,
            element.Height * 0.78, SurfaceKind.DoorLeaf);
        yield return RoundBlock(element, 0, 0, diameter, element.Height * 0.48, element.Height);
        yield return RoundBlock(element, -diameter * 0.20, diameter * 0.12, diameter * 0.62,
            element.Height * 0.38, element.Height * 0.78);
        yield return RoundBlock(element, diameter * 0.22, -diameter * 0.10, diameter * 0.58,
            element.Height * 0.42, element.Height * 0.82);
    }

    /// <summary>Builds a cabinet carcass, toe kick, countertop or top cap, and paired door fronts.</summary>
    private static IEnumerable<ElementPart> Cabinet(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        var toe = element.ComponentShape == ResidentialComponentShape.WallCabinet ? 0 : Math.Min(0.10, element.Height / 6);
        yield return Block(element, 0, 0, width, depth, toe, element.Height - 0.04, SurfaceKind.DoorLeaf);
        if (toe > 0) yield return Block(element, 0, depth * 0.08, width * 0.88, depth * 0.82, 0, toe, SurfaceKind.Structure);
        yield return Block(element, 0, 0, width + 0.04, depth + 0.04, element.Height - 0.04, element.Height, SurfaceKind.Floor);
        foreach (var side in new[] { -1, 1 })
            yield return Block(element, side * width * 0.25, -depth / 2 - 0.006, width * 0.47, 0.012,
                toe + 0.02, element.Height - 0.08, SurfaceKind.DoorLeaf);
    }

    /// <summary>Builds a refrigerator body with split doors and projecting handles.</summary>
    private static IEnumerable<ElementPart> Refrigerator(StructuralElement element)
    {
        yield return Block(element, 0, 0, Width(element), Depth(element), 0, element.Height, SurfaceKind.OpeningFrame);
        foreach (var side in new[] { -1, 1 })
        {
            yield return Block(element, side * Width(element) * 0.25, -Depth(element) / 2 - 0.012,
                Width(element) * 0.48, 0.024, 0.05, element.Height - 0.05, SurfaceKind.Structure);
            yield return Block(element, side * Width(element) * 0.09, -Depth(element) / 2 - 0.035,
                0.025, 0.025, element.Height * 0.42, element.Height * 0.78, SurfaceKind.OpeningFrame);
        }
    }

    /// <summary>Builds a range body, cooktop, back control panel, and four burners.</summary>
    private static IEnumerable<ElementPart> Range(StructuralElement element)
    {
        yield return Block(element, 0, 0, Width(element), Depth(element), 0, element.Height * 0.82, SurfaceKind.OpeningFrame);
        yield return Block(element, 0, 0, Width(element), Depth(element), element.Height * 0.82,
            element.Height * 0.87, SurfaceKind.Structure);
        yield return Block(element, 0, Depth(element) * 0.42, Width(element), Depth(element) * 0.14,
            element.Height * 0.87, element.Height, SurfaceKind.OpeningFrame);
        foreach (var (x, y) in FourQuadrants(element, 0.24))
            yield return RoundBlock(element, x, y, Math.Min(Width(element), Depth(element)) * 0.22,
                element.Height * 0.87, element.Height * 0.89, SurfaceKind.Structure);
    }

    /// <summary>Builds a freestanding appliance with a recessed front panel and round loading door.</summary>
    private static IEnumerable<ElementPart> Appliance(StructuralElement element)
    {
        yield return Block(element, 0, 0, Width(element), Depth(element), 0, element.Height, SurfaceKind.OpeningFrame);
        yield return Block(element, 0, -Depth(element) / 2 - 0.012, Width(element) * 0.86, 0.024,
            element.Height * 0.12, element.Height * 0.82, SurfaceKind.Structure);
        if (element.ComponentShape is ResidentialComponentShape.WasherDryer or ResidentialComponentShape.ClothesDryer)
            yield return RoundBlock(element, 0, -Depth(element) / 2 - 0.032, Width(element) * 0.52,
                element.Height * 0.22, element.Height * 0.68, SurfaceKind.Glass);
    }

    /// <summary>Builds a vanity cabinet and counter with one basin and mixer, or two on a vanity of 1.3 m or more.</summary>
    private static IEnumerable<ElementPart> Vanity(StructuralElement element)
    {
        foreach (var part in Cabinet(element)) yield return part;
        var basins = Width(element) >= 1.3 ? new[] { -Width(element) / 4, Width(element) / 4 } : new[] { 0.0 };
        var basinWidth = Math.Min(Width(element) / basins.Length * 0.62, 0.50);
        foreach (var x in basins)
        {
            // A vessel basin: a ceramic bowl standing on the counter, hollow inside.
            yield return Soft(element, x, -Depth(element) * 0.06, basinWidth, Depth(element) * 0.62,
                element.Height, element.Height + 0.02, 0.08, SurfaceKind.Ceramic);
            foreach (var part in Frame(element, x, -Depth(element) * 0.06, basinWidth, Depth(element) * 0.62,
                         basinWidth - 0.03, Depth(element) * 0.62 - 0.03, element.Height + 0.02, element.Height + 0.14,
                         SurfaceKind.Ceramic))
                yield return part;
            foreach (var part in Faucet(element, x, Depth(element) * 0.36, element.Height, 0.14, 0.24)) yield return part;
        }
    }

    /// <summary>Builds a bathtub from four rim panels around an open inner volume.</summary>
    private static IEnumerable<ElementPart> Bathtub(StructuralElement element) => RimmedContainer(element, 0.08, 0.12, element.Height);

    /// <summary>Builds a shower tray and two transparent enclosure panels.</summary>
    private static IEnumerable<ElementPart> Shower(StructuralElement element)
    {
        yield return Block(element, 0, 0, Width(element), Depth(element), 0, 0.08, SurfaceKind.Ceramic);
        yield return Block(element, 0, Depth(element) / 2 - 0.01, Width(element), 0.02, 0.08, element.Height, SurfaceKind.Glass);
        yield return Block(element, Width(element) / 2 - 0.01, 0, 0.02, Depth(element), 0.08, element.Height, SurfaceKind.Glass);
    }

    /// <summary>Builds a desk top, two supports, and a shallow drawer bank.</summary>
    private static IEnumerable<ElementPart> Desk(StructuralElement element)
    {
        yield return Block(element, 0, 0, Width(element), Depth(element), element.Height - 0.06, element.Height,
            SurfaceKind.DoorLeaf);
        foreach (var side in new[] { -1, 1 })
            yield return Block(element, side * Width(element) * 0.43, 0, 0.08, Depth(element) * 0.85,
                0, element.Height - 0.06, SurfaceKind.DoorLeaf);
        yield return Block(element, Width(element) * 0.24, -Depth(element) * 0.08, Width(element) * 0.34,
            Depth(element) * 0.72, element.Height * 0.46, element.Height - 0.08, SurfaceKind.DoorLeaf);
    }

    /// <summary>Builds a dresser or nightstand body with horizontal drawer fronts and a top cap.</summary>
    private static IEnumerable<ElementPart> Dresser(StructuralElement element)
    {
        yield return Block(element, 0, 0, Width(element), Depth(element), 0.06, element.Height - 0.04, SurfaceKind.DoorLeaf);
        yield return Block(element, 0, 0, Width(element) + 0.03, Depth(element) + 0.03,
            element.Height - 0.04, element.Height, SurfaceKind.DoorLeaf);
        for (var index = 0; index < 3; index++)
            yield return Block(element, 0, -Depth(element) / 2 - 0.008, Width(element) * 0.90, 0.016,
                0.10 + index * (element.Height - 0.16) / 3, 0.10 + (index + 1) * (element.Height - 0.16) / 3 - 0.015,
                SurfaceKind.Structure);
    }

    /// <summary>Builds a bookshelf with side panels, back, and evenly spaced shelves.</summary>
    private static IEnumerable<ElementPart> Bookshelf(StructuralElement element)
    {
        const double panel = 0.035;
        foreach (var side in new[] { -1, 1 })
            yield return Block(element, side * (Width(element) / 2 - panel / 2), 0, panel, Depth(element),
                0, element.Height, SurfaceKind.DoorLeaf);
        yield return Block(element, 0, Depth(element) / 2 - panel / 2, Width(element), panel, 0, element.Height,
            SurfaceKind.DoorLeaf);
        var shelfCount = Math.Max(3, (int)Math.Round(element.Height / 0.38));
        for (var index = 0; index <= shelfCount; index++)
        {
            var level = element.Height * index / shelfCount;
            yield return Block(element, 0, 0, Width(element), Depth(element), Math.Max(0, level - panel), level,
                SurfaceKind.DoorLeaf);
        }
    }

    /// <summary>Builds a thin television screen with a central pedestal and base.</summary>
    private static IEnumerable<ElementPart> Television(StructuralElement element)
    {
        yield return Block(element, 0, 0, Width(element), Depth(element), 0, element.Height, SurfaceKind.OpeningFrame);
        yield return Block(element, 0, -Depth(element) / 2 - 0.006, Width(element) * 0.94, 0.012,
            element.Height * 0.06, element.Height * 0.94, SurfaceKind.Glass);
        yield return Block(element, 0, 0, 0.05, Depth(element), -0.18, 0, SurfaceKind.OpeningFrame);
        yield return Block(element, 0, 0, Width(element) * 0.30, Depth(element) * 1.8, -0.20, -0.18,
            SurfaceKind.OpeningFrame);
    }

    /// <summary>Builds a floor lamp with a round base, slender stem, and octagonal shade.</summary>
    private static IEnumerable<ElementPart> FloorLamp(StructuralElement element)
    {
        var diameter = Math.Min(Width(element), Depth(element));
        yield return RoundBlock(element, 0, 0, diameter * 0.65, 0, 0.04, SurfaceKind.OpeningFrame);
        yield return RoundBlock(element, 0, 0, 0.025, 0.04, element.Height * 0.78, SurfaceKind.OpeningFrame);
        // The fabric shade glows with the bulb inside it, so it is the emitting surface.
        yield return RoundBlock(element, 0, 0, diameter, element.Height * 0.72, element.Height, SurfaceKind.LightEmitter);
    }

    /// <summary>Builds a shallow ceiling-mounted light: a metal canopy against the ceiling and a glowing diffuser under it.</summary>
    private static IEnumerable<ElementPart> CeilingLight(StructuralElement element)
    {
        var diameter = Math.Min(Width(element), Depth(element));
        yield return RoundBlock(element, 0, 0, diameter * 0.82, 0, element.Height * 0.6, SurfaceKind.LightEmitter);
        yield return RoundBlock(element, 0, 0, diameter, element.Height * 0.6, element.Height, SurfaceKind.Metal);
    }

    /// <summary>Builds a barbecue cabinet with a raised hood, cooking grate, side shelves, and legs.</summary>
    private static IEnumerable<ElementPart> Barbecue(StructuralElement element)
    {
        yield return Block(element, 0, 0, Width(element) * 0.62, Depth(element), 0.42, element.Height * 0.76,
            SurfaceKind.OpeningFrame);
        yield return Block(element, 0, Depth(element) * 0.10, Width(element) * 0.62, Depth(element) * 0.70,
            element.Height * 0.76, element.Height, SurfaceKind.Structure);
        foreach (var side in new[] { -1, 1 })
        {
            yield return Block(element, side * Width(element) * 0.41, 0, Width(element) * 0.18, Depth(element) * 0.72,
                element.Height * 0.62, element.Height * 0.68, SurfaceKind.OpeningFrame);
            yield return Block(element, side * Width(element) * 0.23, 0, 0.05, 0.05, 0, 0.42, SurfaceKind.OpeningFrame);
        }
    }

    /// <summary>Builds a pedestal dining or bistro table: a round top on a column and a weighted round foot.</summary>
    private static IEnumerable<ElementPart> RoundTable(StructuralElement element)
    {
        var diameter = Math.Min(Width(element), Depth(element));
        yield return RoundBlock(element, 0, 0, diameter, element.Height - 0.035, element.Height, SurfaceKind.DoorLeaf);
        yield return RoundBlock(element, 0, 0, diameter * 0.12, element.Height - 0.06, element.Height - 0.035, SurfaceKind.DoorLeaf);
        yield return RoundBlock(element, 0, 0, 0.08, 0.04, element.Height - 0.06, SurfaceKind.Metal);
        yield return RoundBlock(element, 0, 0, diameter * 0.52, 0, 0.04, SurfaceKind.Metal);
    }

    /// <summary>
    /// Builds an L-shaped sectional: a back run across the full width and a chaise along the left that runs to the front,
    /// with arms, seat and back cushions.
    /// </summary>
    private static IEnumerable<ElementPart> Sectional(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        var seat = Math.Min(0.95, depth * 0.55);
        var chaise = Math.Min(0.95, width * 0.4);
        const double arm = 0.16;
        const double back = 0.26;
        const SurfaceKind fabric = SurfaceKind.Upholstery;
        yield return Soft(element, 0, depth / 2 - seat / 2, width, seat, 0.12, 0.38, 0.04, fabric);
        yield return Soft(element, -width / 2 + chaise / 2, -seat / 2, chaise, depth - seat, 0.12, 0.38, 0.04, fabric);
        yield return Soft(element, arm / 2, depth / 2 - back / 2, width - arm, back, 0.32, element.Height, 0.06, fabric);
        yield return Soft(element, width / 2 - arm / 2, depth / 2 - seat / 2, arm, seat, 0.20, element.Height * 0.72, 0.04, fabric);
        yield return Soft(element, -width / 2 + arm / 2, 0, arm, depth, 0.20, element.Height * 0.72, 0.04, fabric);
        foreach (var (x, y) in Corners(element, 0.08))
            yield return Block(element, x, y, 0.04, 0.04, 0, 0.12, SurfaceKind.DoorLeaf);
        // Chaise cushion, then seat and back cushions along the back run.
        yield return Soft(element, -width / 2 + arm + (chaise - arm) / 2, -seat / 2 + 0.02, chaise - arm - 0.02,
            depth - seat - 0.02, 0.38, 0.52, 0.05, fabric);
        var runStart = -width / 2 + chaise;
        var runLength = width / 2 - arm - runStart;
        var count = Math.Clamp((int)Math.Round(runLength / 0.70), 1, 4);
        var cushion = (runLength - 0.03 * (count - 1)) / count;
        for (var index = 0; index < count; index++)
        {
            var x = runStart + cushion / 2 + index * (cushion + 0.03);
            yield return Soft(element, x, depth / 2 - back - (seat - back) / 2 + 0.02, cushion, seat - back - 0.02,
                0.38, 0.52, 0.05, fabric);
        }
        var backCount = Math.Clamp((int)Math.Round((width - 2 * arm) / 0.75), 1, 5);
        var backWidth = (width - 2 * arm - 0.03 * (backCount - 1)) / backCount;
        for (var index = 0; index < backCount; index++)
            yield return Soft(element, -width / 2 + arm + backWidth / 2 + index * (backWidth + 0.03), depth / 2 - back - 0.07,
                backWidth, 0.16, 0.50, element.Height - 0.04, 0.05, fabric);
    }

    /// <summary>Builds a task chair: a five-star base on casters, gas column, upholstered seat and back, and armrests.</summary>
    private static IEnumerable<ElementPart> OfficeChair(StructuralElement element)
    {
        var radius = Math.Min(Width(element), Depth(element)) / 2 * 0.92;
        for (var index = 0; index < 5; index++)
        {
            var angle = Math.PI / 2 + index * 2 * Math.PI / 5;
            var (x, y) = (Math.Cos(angle) * radius, Math.Sin(angle) * radius);
            yield return Bar(element, 0, 0, x, y, 0.04, 0.05, 0.09, SurfaceKind.Metal);
            yield return RoundBlock(element, x, y, 0.05, 0, 0.05, SurfaceKind.OpeningFrame);
        }
        yield return RoundBlock(element, 0, 0, 0.06, 0.09, 0.42, SurfaceKind.Metal);
        var seat = Math.Min(Width(element) * 0.8, 0.52);
        yield return Soft(element, 0, -0.02, seat, seat * 0.95, 0.42, 0.50, 0.05, SurfaceKind.Upholstery);
        yield return Block(element, 0, seat * 0.46, 0.06, 0.04, 0.45, 0.64, SurfaceKind.Metal);
        yield return Soft(element, 0, seat * 0.48, seat * 0.9, 0.07, 0.60, element.Height, 0.03, SurfaceKind.Upholstery);
        foreach (var side in new[] { -1, 1 })
        {
            yield return Block(element, side * seat * 0.52, 0, 0.035, 0.035, 0.48, 0.66, SurfaceKind.Metal);
            yield return Soft(element, side * seat * 0.52, 0, 0.06, 0.26, 0.66, 0.69, 0.02, SurfaceKind.OpeningFrame);
        }
    }

    /// <summary>Builds a counter-height stool: round foot, column, footrest ring and a padded round seat.</summary>
    private static IEnumerable<ElementPart> BarStool(StructuralElement element)
    {
        var diameter = Math.Min(Width(element), Depth(element));
        yield return RoundBlock(element, 0, 0, diameter * 0.9, 0, 0.02, SurfaceKind.Metal);
        yield return RoundBlock(element, 0, 0, 0.05, 0.02, element.Height - 0.06, SurfaceKind.Metal);
        const int segments = 12;
        var ring = diameter * 0.36;
        for (var index = 0; index < segments; index++)
        {
            var a = index * 2 * Math.PI / segments;
            var b = (index + 1) * 2 * Math.PI / segments;
            yield return Bar(element, Math.Cos(a) * ring, Math.Sin(a) * ring, Math.Cos(b) * ring, Math.Sin(b) * ring,
                0.016, element.Height * 0.38, element.Height * 0.38 + 0.016, SurfaceKind.Metal);
        }
        foreach (var angle in new[] { 0, Math.PI / 2, Math.PI, 3 * Math.PI / 2 })
            yield return Bar(element, 0, 0, Math.Cos(angle) * ring, Math.Sin(angle) * ring, 0.012,
                element.Height * 0.38, element.Height * 0.38 + 0.012, SurfaceKind.Metal);
        yield return RoundBlock(element, 0, 0, diameter, element.Height - 0.06, element.Height, SurfaceKind.Upholstery);
    }

    /// <summary>Builds an upholstered footstool on four short legs.</summary>
    private static IEnumerable<ElementPart> Ottoman(StructuralElement element)
    {
        yield return Soft(element, 0, 0, Width(element), Depth(element), 0.06, element.Height, 0.06, SurfaceKind.Upholstery);
        foreach (var (x, y) in Corners(element, 0.07))
            yield return Block(element, x, y, 0.035, 0.035, 0, 0.06, SurfaceKind.DoorLeaf);
    }

    /// <summary>Builds a bunk bed: four posts, two decks with mattresses and pillows, a guard rail and a side ladder.</summary>
    private static IEnumerable<ElementPart> BunkBed(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        const double post = 0.07;
        foreach (var (x, y) in Corners(element, post / 2))
            yield return Block(element, x, y, post, post, 0, element.Height, SurfaceKind.DoorLeaf);
        var upper = Math.Max(1.0, element.Height - 0.55);
        foreach (var deck in new[] { 0.20, upper })
        {
            foreach (var side in new[] { -1, 1 })
            {
                yield return Block(element, side * (width / 2 - 0.025), 0, 0.05, depth - 2 * post, deck, deck + 0.15, SurfaceKind.DoorLeaf);
                yield return Block(element, 0, side * (depth / 2 - 0.025), width - 2 * post, 0.05, deck, deck + 0.30, SurfaceKind.DoorLeaf);
            }
            yield return Block(element, 0, 0, width - 0.1, depth - 0.1, deck + 0.04, deck + 0.06, SurfaceKind.DoorLeaf);
            yield return Soft(element, 0, 0, width - 0.1, depth - 0.12, deck + 0.06, deck + 0.22, 0.05, SurfaceKind.Bedding);
            yield return Soft(element, 0, -depth * 0.12, width - 0.08, depth * 0.62, deck + 0.10, deck + 0.25, 0.05, SurfaceKind.Upholstery);
            yield return Soft(element, 0, depth / 2 - 0.25, width * 0.55, 0.32, deck + 0.22, deck + 0.33, 0.06, SurfaceKind.Bedding);
        }
        // Guard rail along the open side of the top bunk, ladder beside it.
        yield return Block(element, -width / 2 + 0.025, depth * 0.12, 0.04, depth * 0.6, upper + 0.38, upper + 0.44, SurfaceKind.DoorLeaf);
        var ladderY = -depth / 2 + 0.35;
        foreach (var side in new[] { -1, 1 })
            yield return Block(element, -width / 2 - 0.02, ladderY + side * 0.2, 0.04, 0.04, 0, upper + 0.45, SurfaceKind.DoorLeaf);
        for (var z = 0.30; z < upper + 0.3; z += 0.28)
            yield return Block(element, -width / 2 - 0.02, ladderY, 0.03, 0.40, z, z + 0.03, SurfaceKind.DoorLeaf);
    }

    /// <summary>Builds a crib: corner posts, top and bottom rails, slats every 7 cm all round, and a raised mattress.</summary>
    private static IEnumerable<ElementPart> Crib(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        const double post = 0.05;
        foreach (var (x, y) in Corners(element, post / 2))
            yield return Block(element, x, y, post, post, 0, element.Height, SurfaceKind.DoorLeaf);
        foreach (var (bottom, top) in new[] { (0.22, 0.27), (element.Height - 0.05, element.Height) })
            foreach (var side in new[] { -1, 1 })
            {
                yield return Block(element, side * (width / 2 - 0.02), 0, 0.04, depth - 2 * post, bottom, top, SurfaceKind.DoorLeaf);
                yield return Block(element, 0, side * (depth / 2 - 0.02), width - 2 * post, 0.04, bottom, top, SurfaceKind.DoorLeaf);
            }
        foreach (var side in new[] { -1, 1 })
        {
            for (var y = -depth / 2 + post + 0.06; y < depth / 2 - post - 0.03; y += 0.07)
                yield return Block(element, side * (width / 2 - 0.02), y, 0.02, 0.02, 0.27, element.Height - 0.05, SurfaceKind.DoorLeaf);
            for (var x = -width / 2 + post + 0.06; x < width / 2 - post - 0.03; x += 0.07)
                yield return Block(element, x, side * (depth / 2 - 0.02), 0.02, 0.02, 0.27, element.Height - 0.05, SurfaceKind.DoorLeaf);
        }
        yield return Soft(element, 0, 0, width - 0.08, depth - 0.08, 0.30, 0.42, 0.04, SurfaceKind.Bedding);
    }

    /// <summary>Builds a sink base: cabinet and doors, a counter around an undermounted steel bowl, and a gooseneck mixer.</summary>
    private static IEnumerable<ElementPart> KitchenSink(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        var height = element.Height;
        var toe = Math.Min(0.10, height / 6);
        yield return Block(element, 0, 0, width, depth, toe, height - 0.24, SurfaceKind.DoorLeaf);
        yield return Block(element, 0, depth * 0.08, width * 0.88, depth * 0.82, 0, toe, SurfaceKind.Structure);
        foreach (var side in new[] { -1, 1 })
        {
            yield return Block(element, side * width / 2 - side * 0.01, 0, 0.02, depth, height - 0.24, height - 0.04, SurfaceKind.DoorLeaf);
            yield return Block(element, side * width * 0.25, -depth / 2 - 0.006, width * 0.47, 0.012,
                toe + 0.02, height - 0.08, SurfaceKind.DoorLeaf);
        }
        var bowlWidth = Math.Min(width - 0.20, 0.80);
        var bowlDepth = Math.Min(depth - 0.18, 0.42);
        const double bowlY = -0.02;
        foreach (var part in Frame(element, 0, bowlY, width + 0.04, depth + 0.04, bowlWidth, bowlDepth, height - 0.04, height, SurfaceKind.Floor))
            yield return part;
        // The steel bowl hangs under the counter: thin walls and a floor 20 cm down.
        foreach (var part in Frame(element, 0, bowlY, bowlWidth, bowlDepth, bowlWidth - 0.02, bowlDepth - 0.02, height - 0.22, height - 0.04, SurfaceKind.Metal))
            yield return part;
        yield return Block(element, 0, bowlY, bowlWidth, bowlDepth, height - 0.23, height - 0.22, SurfaceKind.Metal);
        foreach (var part in Faucet(element, 0, bowlY + bowlDepth / 2 + (depth / 2 - bowlY - bowlDepth / 2) / 2, height, 0.22, 0.34))
            yield return part;
    }

    /// <summary>Builds a wall-mounted chimney hood: a shallow steel canopy, a tapered body, a chimney and a filter face.</summary>
    private static IEnumerable<ElementPart> RangeHood(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        yield return Block(element, 0, 0, width, depth, 0, 0.06, SurfaceKind.Metal);
        yield return Block(element, 0, depth * 0.12, width * 0.72, depth * 0.76, 0.06, 0.20, SurfaceKind.Metal);
        yield return Block(element, 0, depth / 2 - 0.14, Math.Min(0.30, width * 0.4), 0.26, 0.20, element.Height, SurfaceKind.Metal);
        yield return Block(element, 0, 0, width * 0.9, depth * 0.8, -0.004, 0, SurfaceKind.OpeningFrame);
    }

    /// <summary>Builds a pedestal basin: a column, a ceramic bowl with a hollow, and a mixer on the back deck.</summary>
    private static IEnumerable<ElementPart> PedestalSink(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        var basin = element.Height - 0.16;
        yield return Soft(element, 0, depth * 0.08, width * 0.32, depth * 0.40, 0, basin, 0.06, SurfaceKind.Ceramic);
        yield return Soft(element, 0, 0, width, depth, basin, basin + 0.05, 0.08, SurfaceKind.Ceramic);
        foreach (var part in Frame(element, 0, -0.01, width, depth - 0.02, width * 0.66, depth * 0.52, basin + 0.05,
                     element.Height, SurfaceKind.Ceramic))
            yield return part;
        foreach (var part in Faucet(element, 0, depth / 2 - 0.06, element.Height, 0.10, 0.14)) yield return part;
    }

    /// <summary>Builds a floor-standing bidet: pedestal, open bowl and a deck mixer.</summary>
    private static IEnumerable<ElementPart> Bidet(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        yield return Soft(element, 0, depth * 0.06, width * 0.6, depth * 0.55, 0, element.Height - 0.10, 0.06, SurfaceKind.Ceramic);
        yield return Soft(element, 0, 0, width, depth, element.Height - 0.10, element.Height - 0.07, 0.12, SurfaceKind.Ceramic);
        foreach (var part in Frame(element, 0, -0.02, width, depth - 0.04, width * 0.7, depth * 0.62,
                     element.Height - 0.07, element.Height, SurfaceKind.Ceramic))
            yield return part;
        foreach (var part in Faucet(element, 0, depth / 2 - 0.05, element.Height, 0.07, 0.08)) yield return part;
    }

    /// <summary>Builds a wall-hung urinal: back plate, a ceramic shell open at the front, and the flush valve above.</summary>
    private static IEnumerable<ElementPart> Urinal(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        yield return Block(element, 0, depth / 2 - 0.04, width, 0.08, 0, element.Height, SurfaceKind.Ceramic);
        yield return Soft(element, 0, 0, width, depth, 0, 0.06, 0.10, SurfaceKind.Ceramic);
        foreach (var side in new[] { -1, 1 })
            yield return Soft(element, side * (width / 2 - 0.03), 0, 0.06, depth - 0.02, 0.06, element.Height * 0.85, 0.03, SurfaceKind.Ceramic);
        yield return Block(element, 0, -depth / 2 + 0.03, width - 0.08, 0.06, 0.06, 0.24, SurfaceKind.Ceramic);
        yield return RoundBlock(element, 0, depth / 2 - 0.03, 0.03, element.Height, element.Height + 0.38, SurfaceKind.Metal);
        yield return RoundBlock(element, 0, depth / 2 - 0.06, 0.07, element.Height + 0.20, element.Height + 0.30, SurfaceKind.Metal);
    }

    /// <summary>Builds a wall mirror: a slim frame with the silvered glass on its face.</summary>
    private static IEnumerable<ElementPart> Mirror(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        yield return Block(element, 0, 0.004, width, depth - 0.008, 0, element.Height, SurfaceKind.Metal);
        yield return Block(element, 0, -depth / 2 + 0.003, width - 0.03, 0.006, 0.015, element.Height - 0.015, SurfaceKind.Mirror);
    }

    /// <summary>Builds a tank water heater: enamelled cylinder on legs, steel top, control box and pipe connections.</summary>
    private static IEnumerable<ElementPart> WaterHeater(StructuralElement element)
    {
        var diameter = Math.Min(Width(element), Depth(element));
        foreach (var angle in new[] { Math.PI / 2, Math.PI * 7 / 6, Math.PI * 11 / 6 })
            yield return Block(element, Math.Cos(angle) * diameter * 0.32, Math.Sin(angle) * diameter * 0.32, 0.03, 0.03, 0, 0.06, SurfaceKind.Metal);
        yield return RoundBlock(element, 0, 0, diameter, 0.06, element.Height - 0.06, SurfaceKind.Ceramic);
        yield return RoundBlock(element, 0, 0, diameter * 0.92, element.Height - 0.06, element.Height, SurfaceKind.Metal);
        yield return Block(element, 0, -diameter / 2 + 0.01, 0.12, 0.05, 0.22, 0.38, SurfaceKind.OpeningFrame);
        foreach (var side in new[] { -1, 1 })
            yield return RoundBlock(element, side * diameter * 0.2, 0, 0.025, element.Height, element.Height + 0.30, SurfaceKind.Metal);
    }

    /// <summary>
    /// Builds a hammock on a timber stand: base beam and feet, end posts, ropes, and a fabric bed that sags between them.
    /// </summary>
    private static IEnumerable<ElementPart> Hammock(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        var postX = width / 2 - 0.06;
        yield return Block(element, 0, 0, width - 0.1, 0.08, 0, 0.08, SurfaceKind.DoorLeaf);
        foreach (var side in new[] { -1, 1 })
        {
            yield return Block(element, side * postX, 0, 0.10, depth, 0, 0.07, SurfaceKind.DoorLeaf);
            yield return Block(element, side * postX, 0, 0.08, 0.08, 0.07, element.Height, SurfaceKind.DoorLeaf);
        }
        var end = width * 0.30;
        const double low = 0.45;
        var high = Math.Min(element.Height - 0.35, 0.85);
        const int segments = 16;
        for (var index = 0; index < segments; index++)
        {
            var x0 = -end + 2 * end * index / segments;
            var x1 = -end + 2 * end * (index + 1) / segments;
            var middle = (x0 + x1) / 2 / end;
            var z = low + (high - low) * middle * middle;
            var spread = depth * 0.78 * (1 - 0.55 * middle * middle);
            yield return Block(element, (x0 + x1) / 2, 0, x1 - x0 + 0.01, spread, z, z + 0.015, SurfaceKind.Upholstery);
        }
        // Ropes from each post top down to the gathered end of the fabric.
        foreach (var side in new[] { -1, 1 })
        {
            const int steps = 8;
            for (var step = 0; step < steps; step++)
            {
                var t = (step + 0.5) / steps;
                var x = side * (postX - (postX - end) * t);
                var z = element.Height - 0.08 - (element.Height - 0.08 - high) * t;
                yield return Block(element, x, 0, (postX - end) / steps + 0.01, 0.012, z - 0.012, z, SurfaceKind.Bark);
            }
        }
    }

    /// <summary>Builds a reclining sun lounger: legs, a slatted bed, a cushion and a backrest raised at the head.</summary>
    private static IEnumerable<ElementPart> SunLounger(StructuralElement element)
    {
        var width = Width(element);
        var depth = Depth(element);
        foreach (var (x, y) in Corners(element, 0.06))
            yield return Block(element, x, y, 0.04, 0.04, 0, 0.30, SurfaceKind.DoorLeaf);
        var hinge = depth * 0.18;
        yield return Block(element, 0, (-depth / 2 + hinge) / 2, width, hinge + depth / 2, 0.30, 0.34, SurfaceKind.DoorLeaf);
        yield return Soft(element, 0, (-depth / 2 + hinge) / 2, width - 0.06, hinge + depth / 2 - 0.04, 0.34, 0.40, 0.04, SurfaceKind.Upholstery);
        const int slices = 7;
        var back = depth / 2 - hinge;
        for (var index = 0; index < slices; index++)
        {
            var y = hinge + back * (index + 0.5) / slices;
            var z = 0.40 + (element.Height - 0.40) * (index + 1) / slices;
            yield return Soft(element, 0, y, width - 0.06, back / slices + 0.01, z - 0.10, z, 0.02, SurfaceKind.Upholstery);
        }
    }

    /// <summary>Builds a patio umbrella: weighted base, pole, and a stepped conical canopy with a finial.</summary>
    private static IEnumerable<ElementPart> Parasol(StructuralElement element)
    {
        var diameter = Math.Min(Width(element), Depth(element));
        yield return RoundBlock(element, 0, 0, 0.45, 0, 0.08, SurfaceKind.Structure);
        yield return RoundBlock(element, 0, 0, 0.04, 0.08, element.Height, SurfaceKind.DoorLeaf);
        const int tiers = 6;
        var bottom = element.Height - 0.48;
        for (var tier = 0; tier < tiers; tier++)
            yield return RoundBlock(element, 0, 0, diameter * (1 - tier * 0.16), bottom + tier * 0.07, bottom + tier * 0.07 + 0.07,
                SurfaceKind.Upholstery);
        yield return RoundBlock(element, 0, 0, 0.05, element.Height, element.Height + 0.08, SurfaceKind.DoorLeaf);
    }

    /// <summary>
    /// Builds a sedan from horizontal slices: tyres stepped to a round side profile, a body cut away at the wheel arches,
    /// a bonnet and boot, a glasshouse that tapers toward the roof over a dark cabin, head and tail lamps.
    /// </summary>
    private static IEnumerable<ElementPart> Car(StructuralElement element, SurfaceKind paint)
    {
        var width = Width(element);
        var length = Depth(element);
        var height = element.Height;
        var axle = length / 2 - length * 0.19;
        const double tyre = 0.66;
        const double tread = 0.22;
        const int slices = 6;
        foreach (var y in new[] { -axle, axle })
        foreach (var side in new[] { -1, 1 })
        {
            var x = side * (width / 2 - tread / 2 - 0.01);
            for (var slice = 0; slice < slices; slice++)
            {
                var middle = (slice + 0.5) / slices * tyre - tyre / 2;
                var chord = 2 * Math.Sqrt(tyre * tyre / 4 - middle * middle);
                yield return Block(element, x, y, tread, chord, slice * tyre / slices, (slice + 1) * tyre / slices, SurfaceKind.Rubber);
            }
            yield return Block(element, x + side * 0.005, y, tread, 0.36, tyre / 2 - 0.12, tyre / 2 + 0.12, SurfaceKind.Metal);
        }

        // Sills and bumpers below the arches, then the full-length shoulder line above them.
        const double sill = 0.20;
        const double arch = 0.48;
        var archHalf = tyre / 2 + 0.07;
        var front = -length / 2;
        foreach (var (from, to) in new[]
                 {
                     (front + 0.04, -axle - archHalf), (-axle + archHalf, axle - archHalf), (axle + archHalf, length / 2 - 0.04)
                 })
            yield return Block(element, 0, (from + to) / 2, width - 0.04, to - from, sill, arch, paint);
        yield return Block(element, 0, 0, width - 0.02, length - 0.06, arch, height * 0.53, paint);
        yield return Block(element, 0, 0.05, width - 0.10, length - 0.30, height * 0.53, height * 0.57, paint);

        // The glasshouse leans back from the windscreen and in from the doors as it rises.
        const int bands = 5;
        var glassBottom = height * 0.57;
        var roof = height - 0.05;
        for (var band = 0; band < bands; band++)
        {
            var t = (band + 0.5) / bands;
            var start = front + length * 0.33 + length * 0.13 * t;
            var end = length / 2 - length * 0.22 - length * 0.08 * t;
            var bottom = glassBottom + (roof - glassBottom) * band / bands;
            var top = glassBottom + (roof - glassBottom) * (band + 1) / bands;
            yield return Block(element, 0, (start + end) / 2, width - 0.14 - 0.16 * t, end - start, bottom, top, SurfaceKind.Glass);
        }
        var cabinStart = front + length * 0.36;
        var cabinEnd = length / 2 - length * 0.26;
        yield return Block(element, 0, (cabinStart + cabinEnd) / 2, width - 0.40, cabinEnd - cabinStart, glassBottom, roof - 0.08,
            SurfaceKind.Rubber);
        var roofStart = front + length * 0.46;
        var roofEnd = length / 2 - length * 0.30;
        yield return Block(element, 0, (roofStart + roofEnd) / 2, width - 0.32, roofEnd - roofStart, roof, height, paint);

        foreach (var side in new[] { -1, 1 })
        {
            yield return Block(element, side * (width / 2 - 0.22), front + 0.03, 0.30, 0.04, arch + 0.06, arch + 0.16, SurfaceKind.Mirror);
            yield return Block(element, side * (width / 2 - 0.20), length / 2 - 0.03, 0.34, 0.04, arch + 0.08, arch + 0.18,
                SurfaceKind.FireRed);
        }
    }

    /// <summary>Four strips around a rectangular hole: a counter around a sink, the rim of a bowl.</summary>
    private static IEnumerable<ElementPart> Frame(StructuralElement element, double centerX, double centerY, double outerWidth,
        double outerDepth, double innerWidth, double innerDepth, double bottom, double top, SurfaceKind kind)
    {
        var side = (outerWidth - innerWidth) / 2;
        var end = (outerDepth - innerDepth) / 2;
        yield return Block(element, centerX, centerY - innerDepth / 2 - end / 2, outerWidth, end, bottom, top, kind);
        yield return Block(element, centerX, centerY + innerDepth / 2 + end / 2, outerWidth, end, bottom, top, kind);
        yield return Block(element, centerX - innerWidth / 2 - side / 2, centerY, side, innerDepth, bottom, top, kind);
        yield return Block(element, centerX + innerWidth / 2 + side / 2, centerY, side, innerDepth, bottom, top, kind);
    }

    /// <summary>A deck-mounted mixer: escutcheon, riser and a spout reaching toward the front (-Y).</summary>
    private static IEnumerable<ElementPart> Faucet(StructuralElement element, double x, double y, double deck, double reach, double rise)
    {
        yield return RoundBlock(element, x, y, 0.05, deck, deck + 0.025, SurfaceKind.Metal);
        yield return RoundBlock(element, x, y, 0.028, deck, deck + rise, SurfaceKind.Metal);
        yield return Block(element, x, y - reach / 2, 0.024, reach, deck + rise - 0.024, deck + rise, SurfaceKind.Metal);
        yield return RoundBlock(element, x, y - reach + 0.012, 0.03, deck + rise - 0.06, deck + rise - 0.02, SurfaceKind.Metal);
        yield return Block(element, x, y + 0.03, 0.015, 0.06, deck + rise * 0.55, deck + rise * 0.55 + 0.015, SurfaceKind.Metal);
    }

    /// <summary>A straight member of a given thickness between two plan points relative to the insertion point.</summary>
    private static ElementPart Bar(StructuralElement element, double x0, double y0, double x1, double y1, double thickness,
        double bottom, double top, SurfaceKind kind)
    {
        var length = Math.Max(1e-6, Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));
        var normalX = -(y1 - y0) / length * thickness / 2;
        var normalY = (x1 - x0) / length * thickness / 2;
        var (originX, originY) = (element.Start.X, element.Start.Y);
        return new ElementPart(
        [
            new(originX + x0 - normalX, originY + y0 - normalY), new(originX + x1 - normalX, originY + y1 - normalY),
            new(originX + x1 + normalX, originY + y1 + normalY), new(originX + x0 + normalX, originY + y0 + normalY)
        ], element.Offset + bottom, element.Offset + top, kind);
    }

    /// <summary>Returns four wall solids around a rectangular container opening.</summary>
    private static IEnumerable<ElementPart> RimmedContainer(StructuralElement element, double wall, double rim,
        double top)
    {
        yield return Block(element, 0, -Depth(element) / 2 + wall / 2, Width(element), wall, 0, top, SurfaceKind.Ceramic);
        yield return Block(element, 0, Depth(element) / 2 - wall / 2, Width(element), wall, 0, top, SurfaceKind.Ceramic);
        yield return Block(element, -Width(element) / 2 + wall / 2, 0, wall, Depth(element) - 2 * wall, 0, top, SurfaceKind.Ceramic);
        yield return Block(element, Width(element) / 2 - wall / 2, 0, wall, Depth(element) - 2 * wall, 0, top, SurfaceKind.Ceramic);
        yield return Block(element, 0, 0, Width(element) - 2 * rim, Depth(element) - 2 * rim, 0, 0.05, SurfaceKind.Ceramic);
    }

    /// <summary>Returns four inset plan corner centers for legs or burners.</summary>
    private static IEnumerable<(double X, double Y)> Corners(StructuralElement element, double inset)
    {
        foreach (var x in new[] { -Width(element) / 2 + inset, Width(element) / 2 - inset })
        foreach (var y in new[] { -Depth(element) / 2 + inset, Depth(element) / 2 - inset })
            yield return (x, y);
    }

    /// <summary>Returns four symmetric positions at a fraction of the width and depth.</summary>
    private static IEnumerable<(double X, double Y)> FourQuadrants(StructuralElement element, double fraction)
    {
        foreach (var x in new[] { -Width(element) * fraction, Width(element) * fraction })
        foreach (var y in new[] { -Depth(element) * fraction, Depth(element) * fraction })
            yield return (x, y);
    }

    /// <summary>Creates one axis-aligned rectangular solid relative to the component insertion point and offset.</summary>
    private static ElementPart Block(StructuralElement element, double centerX, double centerY, double width,
        double depth, double bottom, double top, SurfaceKind kind = SurfaceKind.Structure) =>
        new(Rectangle(element.Start.X + centerX, element.Start.Y + centerY, width, depth),
            element.Offset + bottom, element.Offset + top, kind);

    /// <summary>
    /// An upholstered block: its plan corners are rounded (chamfered twice), so cushions, mattresses and arms read soft
    /// instead of boxy.
    /// </summary>
    private static ElementPart Soft(StructuralElement element, double centerX, double centerY, double width, double depth,
        double bottom, double top, double round, SurfaceKind kind)
    {
        var r = Math.Min(round, Math.Min(width, depth) * 0.3);
        var x = element.Start.X + centerX;
        var y = element.Start.Y + centerY;
        var points = new List<Point2>();
        foreach (var (cx, cy, start) in new[]
                 {
                     (x + width / 2 - r, y - depth / 2 + r, -Math.PI / 2), (x + width / 2 - r, y + depth / 2 - r, 0.0),
                     (x - width / 2 + r, y + depth / 2 - r, Math.PI / 2), (x - width / 2 + r, y - depth / 2 + r, Math.PI)
                 })
            for (var step = 0; step <= 2; step++)
            {
                var angle = start + step * Math.PI / 4;
                points.Add(new Point2(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
            }
        return new ElementPart(points, element.Offset + bottom, element.Offset + top, kind);
    }

    /// <summary>Creates one regular twelve-sided solid used for pots, stems, bowls, burners, and light fittings.</summary>
    private static ElementPart RoundBlock(StructuralElement element, double centerX, double centerY, double diameter,
        double bottom, double top, SurfaceKind kind = SurfaceKind.Structure) =>
        new(Round(element.Start.X + centerX, element.Start.Y + centerY, diameter, 12),
            element.Offset + bottom, element.Offset + top, kind);

    /// <summary>Returns the effective component width, bounded away from degenerate geometry.</summary>
    private static double Width(StructuralElement element) => Math.Max(0.02, element.Width);

    /// <summary>Returns the effective component depth, falling back to width for legacy square components.</summary>
    private static double Depth(StructuralElement element) => Math.Max(0.02, element.Depth > 0 ? element.Depth : element.Width);

    /// <summary>Creates a counter-clockwise rectangle centered on a plan coordinate.</summary>
    private static IReadOnlyList<Point2> Rectangle(double centerX, double centerY, double width, double depth) =>
    [
        new(centerX - width / 2, centerY - depth / 2), new(centerX + width / 2, centerY - depth / 2),
        new(centerX + width / 2, centerY + depth / 2), new(centerX - width / 2, centerY + depth / 2)
    ];

    /// <summary>Creates a counter-clockwise regular polygon centered on a plan coordinate.</summary>
    private static IReadOnlyList<Point2> Round(double centerX, double centerY, double diameter, int sides) =>
        Enumerable.Range(0, sides)
            .Select(index => 2 * Math.PI * index / sides)
            .Select(angle => new Point2(centerX + Math.Cos(angle) * diameter / 2,
                centerY + Math.Sin(angle) * diameter / 2)).ToArray();
}
