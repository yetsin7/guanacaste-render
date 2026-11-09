namespace Guanacaste3D.Core;

/// <summary>
/// A street-front house: a long white painted volume floats over a recessed glazed living room and a fair-faced
/// concrete carport frame, with a thin concrete canopy above it and a breeze-block (cobogó) screen at its east end.
/// The carport holds a tall timber pivot door, slatted timber cladding and two parked cars; in front lie a lawn with
/// stepping stones, a driveway, the sidewalk and the street, under the shade of a large tree. Like every sample it is
/// built only through <see cref="DocumentSession"/>, so every piece stays editable.
/// </summary>
public static partial class BreezeBlockHouseShowcase
{
    /// <summary>Elevation of the upper floor: the white volume's soffit sits 30 cm below it.</summary>
    public const double UpperElevation = 3.9;

    private static SlabPreset Slab(string name) => ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == name);

    private static ElementPreset Element(string name) => ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == name);

    private static Guid WallType(string name) => ArchitecturalLibrary.WallTypes().Single(type => type.Name == name).Id;

    private static Point2[] Rectangle(double x1, double y1, double x2, double y2) =>
        [new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2)];

    /// <summary>Names of the detailed mesh families the house uses when they are supplied.</summary>
    public const string TreeFamily = "Jacaranda Tree (detailed)";
    public const string SedanFamily = "Sedan Car (detailed)";
    public const string CoupeFamily = "Coupe Car Silver (detailed)";
    public const string BushFamily = "Dense Shrub B (detailed)";
    public const string FernFamily = "Fern (detailed)";
    public const string GrassTuftFamily = "Grass Tuft (detailed)";
    public const string PachiraFamily = "Money Tree Pachira (detailed)";
    public const string CalatheaFamily = "Calathea (detailed)";
    public const string AnthuriumFamily = "Anthurium (detailed)";

    /// <summary>
    /// The render view of the reference photograph: from across the street at eye height, verticals corrected, under the
    /// afternoon sun of 21 December at 14:45 solar time in Guanacaste (12.1° N), which comes from the front left so the
    /// jacaranda's shadow falls across the white volume. Full HD, high quality, contrast look, slight cirrus.
    /// </summary>
    public static Rendering.RenderSetup StreetView { get; } = new()
    {
        CameraX = 7.6, CameraY = -20.5, CameraZ = 1.6, YawDegrees = 90, PitchDegrees = 12, FieldOfViewDegrees = 46,
        Scenario = Rendering.LightingScenario.Sunny, DayOfYear = 355, SolarHour = 14.75, Latitude = 12.1, CloudCover = 0.15,
        Haze = true, Quality = Rendering.RenderQuality.High, Width = 1920, Height = 1080, Look = Rendering.ToneMapper.Aces,
        AutoExposure = true, Bloom = false, Vignette = false, CorrectVerticals = true, ArtificialLights = false,
        Clouds = Rendering.CloudStyle.Cirrus, StyleId = "realistic", Shadows = 0.25, Highlights = -0.2, Vibrance = 0.25,
        Sharpen = 0.35, Glare = 0.15
    };

    /// <summary>The house with procedural trees and cars only.</summary>
    public static BuildingDocument Create() => Create([]);

    /// <summary>
    /// The house; when <paramref name="families"/> holds the detailed jacaranda and car families (shipped as .g3df
    /// files), they are loaded into the project and replace the procedural tree and cars.
    /// </summary>
    public static BuildingDocument Create(IReadOnlyList<FamilyDocument> families)
    {
        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Casa del cobogó: volumen blanco sobre cochera de concreto"));
        var ground = session.Document.Levels.Single(level => level.Role == LevelRole.BaseFloor).Id;
        session.UpdateLevel(ground, "Planta baja", 0);
        var upper = session.AddLevel();
        session.UpdateLevel(upper, "Planta alta", UpperElevation);

        Guid? Load(string name) => families.FirstOrDefault(family => family.Name == name) is { } family
            ? session.LoadComponentFamily(family)
            : null;
        var jacaranda = Load(TreeFamily);
        var sedan = Load(SedanFamily);
        var coupe = Load(CoupeFamily);

        var white = WallType("Rendered Block 150 mm");
        var concrete = WallType("Reinforced Concrete 300 mm");
        var timber = WallType("Timber Clad Wall 200 mm");
        var curtain = Element("Curtain Wall 100 mm");

        // Ground floor, west: a solid fair-faced concrete end, then the living room glazed floor to soffit, set back.
        session.AddWall(ground, new Point2(0, 0.55), new Point2(2.0, 0.55), height: 3.6, wallTypeId: concrete);
        session.AddWall(ground, new Point2(0.15, 0.4), new Point2(0.15, 12), height: 3.6, wallTypeId: concrete);
        session.AddWall(ground, new Point2(1.85, 0.7), new Point2(1.85, 1.5), height: 3.6, wallTypeId: concrete);
        session.AddElementFromPreset(ground, curtain with { Height = 3.6 }, new Point2(2.0, 1.5), new Point2(6.4, 1.5));

        // The carport: a concrete portal (two jambs and a lintel) that stands 40 cm proud of the white volume.
        session.AddWall(ground, new Point2(6.55, -0.4), new Point2(6.55, 1.5), height: 3.1, wallTypeId: concrete);
        session.AddWall(ground, new Point2(6.55, 1.5), new Point2(6.55, 12), height: 3.6, wallTypeId: concrete);
        session.AddWall(ground, new Point2(15.05, -0.4), new Point2(15.05, 5.2), height: 3.1, wallTypeId: concrete);
        session.AddWall(ground, new Point2(15.05, 5.2), new Point2(15.05, 12), height: 3.6, wallTypeId: concrete);
        session.AddSlabFromPreset(ground, Rectangle(6.4, -0.4, 15.2, 5.2), Slab("Exposed Concrete Slab 250 mm"), 3.1);
        // Its back wall is slatted timber with a tall pivot entrance door in the same wood.
        var slats = session.AddWall(ground, new Point2(6.7, 5.2), new Point2(14.9, 5.2), height: 3.6, wallTypeId: timber);
        var pivot = session.Document.OpeningTypes.Single(type => type.FamilyName == "Pivot Door" && Math.Abs(type.Width - 1.6) < 1e-6);
        session.AddOpening(slats, OpeningKind.Door, offset: 1.0, openingTypeId: pivot.Id);
        session.AddWall(ground, new Point2(0.15, 11.85), new Point2(15.05, 11.85), height: 3.6, wallTypeId: white);

        session.AddSlabFromPreset(ground, Rectangle(0, 0.4, 6.4, 12), Slab("Porcelain Tile Floor 200 mm"));
        session.AddSlabFromPreset(ground, Rectangle(6.4, 5.2, 15.2, 12), Slab("Porcelain Tile Floor 200 mm"));
        session.AddSlabFromPreset(ground, Rectangle(6.4, -0.4, 15.2, 5.2), Slab("Concrete Slab 150 mm"));
        session.AddRoom(ground, new Point2(2.1, 1.6), new Point2(6.3, 11.7), "Sala y comedor");
        session.AddRoom(ground, new Point2(6.8, 0), new Point2(14.8, 5.0), "Cochera");

        // The white volume: painted slabs top and bottom, so its soffit and edges read as one rendered box.
        session.AddSlabFromPreset(upper, Rectangle(0, 0, 15.2, 12), Slab("Painted Concrete Slab 300 mm"));
        session.AddWall(upper, new Point2(0, 0.075), new Point2(12.8, 0.075), height: 3, wallTypeId: white);
        session.AddWall(upper, new Point2(0.075, 0), new Point2(0.075, 12), height: 3, wallTypeId: white);
        session.AddWall(upper, new Point2(15.125, 0), new Point2(15.125, 12), height: 3, wallTypeId: white);
        session.AddWall(upper, new Point2(0, 11.925), new Point2(15.2, 11.925), height: 3, wallTypeId: white);
        // The east end of the street facade is a cobogó screen in front of a glazed balcony.
        session.AddElementFromPreset(upper, Element("Breeze Block Screen 150 mm"), new Point2(12.8, 0.075), new Point2(15.05, 0.075));
        session.AddWall(upper, new Point2(12.875, 0.15), new Point2(12.875, 1.6), height: 3, wallTypeId: white);
        session.AddElementFromPreset(upper, curtain, new Point2(12.95, 1.6), new Point2(15.05, 1.6));
        session.AddSlabFromPreset(upper, Rectangle(0, 0, 15.2, 12), Slab("Painted Concrete Slab 300 mm"), 3.3);
        session.AddRoom(upper, new Point2(12.95, 0.2), new Point2(15.0, 1.5), "Balcón tras el cobogó");

        // The canopy floats 30 cm above the volume on slender recessed columns, overhanging it toward the street.
        session.AddSlabFromPreset(upper, Rectangle(1.8, -0.6, 13.7, 9), Slab("Exposed Concrete Slab 250 mm"), 3.85);
        foreach (var x in new[] { 3.5, 7.7, 11.9 })
            session.AddColumn(upper, new Point2(x, 4.5), 0.15, 0.15, 3.6);

        session.AddElementFromPreset(ground, Element("Straight Stair 1200 mm") with { Height = UpperElevation },
            new Point2(9.6, 6.0), new Point2(9.6, 10.8));

        // Two cars parked nose out, and the living room furniture seen through the glass.
        foreach (var (name, family, x) in new[] { ("Sedan Car", sedan, 10.3), ("Sedan Car Silver", coupe, 12.9) })
        {
            var at = new Point2(x, 2.4);
            if (family is Guid id) session.PlaceComponentFamily(ground, id, at);
            else session.AddElementFromPreset(ground, Element(name), at, at);
        }
        foreach (var (name, x, y, turn) in new[]
                 {
                     ("Three Seat Sofa", 4.3, 4.4, 0.0), ("Armchair", 2.8, 2.6, Math.PI / 2), ("Coffee Table", 4.3, 3.2, 0.0)
                 })
            session.AddElementFromPreset(ground, Element(name), new Point2(x, y), new Point2(x, y), turn);

        // The site: lawn and stepping stones to the door, the driveway, sidewalk, grass verge, curb and street.
        var lawn = Slab("Lawn 150 mm");
        var paving = Slab("Concrete Slab 150 mm");
        session.AddSlabFromPreset(ground, Rectangle(-25, -5.6, 8.6, -0.4), lawn);
        session.AddSlabFromPreset(ground, Rectangle(-25, -0.4, 6.4, 0.4), lawn);
        session.AddSlabFromPreset(ground, Rectangle(-25, 0.4, 0, 14), lawn);
        session.AddSlabFromPreset(ground, Rectangle(15.2, -5.6, 35, 14), lawn);
        for (var step = 0; step < 5; step++)
            session.AddSlabFromPreset(ground, Rectangle(6.7, -5.2 + step * 0.95, 8.3, -4.65 + step * 0.95), paving, 0.02);
        // Paving is poured in panels: 1 cm control joints between them read as the dark lines of real concrete.
        void Panels(double x1, double y1, double x2, double y2, int across, int along)
        {
            const double joint = 0.01;
            var (width, depth) = ((x2 - x1) / across, (y2 - y1) / along);
            for (var i = 0; i < across; i++)
            for (var j = 0; j < along; j++)
                session.AddSlabFromPreset(ground, Rectangle(x1 + i * width + joint / 2, y1 + j * depth + joint / 2,
                    x1 + (i + 1) * width - joint / 2, y1 + (j + 1) * depth - joint / 2), paving);
        }
        Panels(8.6, -5.6, 15.2, -0.4, 2, 3);
        Panels(-25, -7.0, 35, -5.6, 40, 1);
        session.AddSlabFromPreset(ground, Rectangle(-25, -7.85, 8.6, -7.0), lawn);
        session.AddSlabFromPreset(ground, Rectangle(15.2, -7.85, 35, -7.0), lawn);
        session.AddSlabFromPreset(ground, Rectangle(8.6, -7.85, 15.2, -7.0), paving);
        session.AddSlabFromPreset(ground, Rectangle(-25, -8.0, 35, -7.85), paving);
        session.AddSlabFromPreset(ground, Rectangle(-25, -22, 35, -8.0), Slab("Asphalt Road 100 mm"), -0.15);
        session.AddSlabFromPreset(ground, Rectangle(-25, -22.15, 35, -22), paving);
        session.AddSlabFromPreset(ground, Rectangle(-25, -34, 35, -22.15), lawn);

        // The large tree in the front lawn, whose shadow falls across the white volume, and street trees opposite.
        var tree = Element("Broadleaf Tree 8 m");
        void Tree(Point2 point, double spread, double height, double turn)
        {
            var id = jacaranda is Guid family
                ? session.PlaceComponentFamily(ground, family, point, turn)
                : session.AddElementFromPreset(ground, tree, point, point, turn);
            var placed = session.Document.StructuralElements.Single(element => element.Id == id);
            session.UpdateStructuralElement(id, placed.Name, spread, height, placed.Offset, placed.Style);
        }
        Tree(new Point2(3.4, -2.6), 13, 13, 0);
        foreach (var (point, turn) in new[] { (new Point2(-5, -26.5), 1.9), (new Point2(7, -27.5), 4.1), (new Point2(18, -26.5), 0.7) })
            Tree(point, 10, 10.5, turn);
        var clipped = Element("Shrub 1.5 m") with { Width = 0.75, Height = 0.6 };
        foreach (var point in new Point2[] { new(6.2, -4.9), new(6.3, -3.6), new(8.9, -4.9), new(9.1, -3.9) })
            session.AddElementFromPreset(ground, clipped, point, point);
        // Under the jacaranda, ferns; at the corners, loose bushes; tufts of grass where the mower misses the lawn edge.
        // Each is a detailed family when it is supplied, otherwise the procedural shrub at the same size.
        void Plant(Guid? family, double scale, double spread, double height, Point2 point, double turn = 0)
        {
            if (family is Guid id) session.PlaceComponentFamily(ground, id, point, turn, scale);
            else session.AddElementFromPreset(ground, Element("Shrub 1.5 m") with { Width = spread, Height = height }, point, point, turn);
        }
        var fern = Load(FernFamily);
        var bush = Load(BushFamily);
        foreach (var (point, turn) in new[] { (new Point2(1.2, -1.0), 0.0), (new Point2(2.6, -0.6), 2.0), (new Point2(4.4, -1.2), 4.1),
                     (new Point2(-0.6, -0.4), 1.0) })
            Plant(fern, 1.8, 1.4, 0.8, point, turn);
        Plant(bush, 0.9, 1.2, 1.2, new Point2(-0.9, 0.9));
        Plant(bush, 0.9, 1.2, 1.2, new Point2(16.2, -0.5), 2.2);
        if (Load(GrassTuftFamily) is Guid tuft)
        {
            var random = new Random(17);
            for (var index = 0; index < 60; index++)
            {
                var along = random.NextDouble();
                var point = index % 2 == 0 ? new Point2(-6 + along * 12.2, -5.5 + random.NextDouble() * 0.2)
                    : new Point2(6.45 + random.NextDouble() * 0.15, -5.4 + along * 5.0);
                session.PlaceComponentFamily(ground, tuft, point, random.NextDouble() * Math.Tau, 1.5 + random.NextDouble());
            }
        }

        // Neighbouring white houses frame the lot on both sides, set back behind their own lawns.
        foreach (var (x1, x2) in new[] { (-14.0, -4.0), (18.5, 28.0) })
        {
            var corners = Rectangle(x1, 2, x2, 14);
            for (var side = 0; side < 4; side++)
                session.AddWall(ground, corners[side], corners[(side + 1) % 4], height: 6.6, wallTypeId: white);
            session.AddSlabFromPreset(ground, corners, Slab("Painted Concrete Slab 300 mm"), 6.9);
        }

        AddInteriors(session, ground, upper, Load);
        session.AddArea(ground, "Área construida planta baja", new Point2(0, 0.4), new Point2(15.2, 12));
        session.AddArea(upper, "Área construida planta alta", new Point2(0, 0), new Point2(15.2, 12));
        AddDrawingSet(session, ground, upper);
        session.StoreRenderSetup(StreetView);
        return session.Document.Clone();
    }
}
