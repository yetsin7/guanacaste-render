namespace Guanacaste3D.Core;

public static partial class BreezeBlockHouseShowcase
{
    /// <summary>
    /// The rooms behind the facade: on the ground floor a hall with the stair, a guest bathroom, laundry and kitchen; on
    /// the upper floor a studio along the street, two bedrooms and a shared bathroom toward the garden, each with its
    /// doors, windows, fixtures and furniture, plus the stairwell, its guard and the sanitary system.
    /// </summary>
    private static void AddInteriors(DocumentSession session, Guid ground, Guid upper, Func<string, Guid?> load)
    {
        var partition = WallType("Gypsum Partition 100 mm");
        Guid Partition(Guid level, double x1, double y1, double x2, double y2) =>
            session.AddWall(level, new Point2(x1, y1), new Point2(x2, y2), height: 3, wallTypeId: partition);
        Guid Existing(Guid level, double x1, double y1, double x2, double y2) => session.Document.Walls.Single(wall =>
            wall.LevelId == level && wall.Start.DistanceTo(new Point2(x1, y1)) < 1e-6 && wall.End.DistanceTo(new Point2(x2, y2)) < 1e-6).Id;
        void Door(Guid wall, double offset, double width = 0.9) => session.AddOpening(wall, OpeningKind.Door, offset, width, height: 2.1);
        void Window(Guid wall, double offset, double width, double height, double sill) =>
            session.AddOpening(wall, OpeningKind.Window, offset, width, sill, height);
        void Place(Guid level, string name, double x, double y, double turn = 0)
        {
            var at = new Point2(x, y);
            session.AddElementFromPreset(level, Element(name), at, at, turn);
        }
        // A family's front faces -Y as authored; these turns face it toward the room from the other walls.
        const double faceWest = -Math.PI / 2, faceNorth = Math.PI;

        // Ground floor. A wide opening joins the living room to the hall; the hall leads to the kitchen and services.
        session.AddOpening(Existing(ground, 6.55, 1.5, 6.55, 12), OpeningKind.Window, 7.0, 2.4, 0, 2.4);
        Door(Partition(ground, 10.8, 5.3, 10.8, 11.75), 4.3);
        var services = Partition(ground, 10.8, 7.8, 14.9, 7.8);
        Door(services, 1.0, 0.8);
        Door(services, 3.0, 0.8);
        Partition(ground, 12.8, 5.3, 12.8, 7.8);
        var back = Existing(ground, 0.15, 11.85, 15.05, 11.85);
        Window(back, 3.15, 2.4, 1.5, 0.9);
        Window(back, 12.85, 1.2, 1.2, 0.9);
        Window(Existing(ground, 0.15, 0.4, 0.15, 12), 7.6, 2.0, 1.4, 0.9);
        session.AddRoom(ground, new Point2(6.9, 5.4), new Point2(10.7, 11.7), "Vestíbulo");
        session.AddRoom(ground, new Point2(10.9, 5.4), new Point2(12.7, 7.7), "Medio baño");
        session.AddRoom(ground, new Point2(12.9, 5.4), new Point2(14.8, 7.7), "Lavandería");
        session.AddRoom(ground, new Point2(10.9, 7.9), new Point2(14.8, 11.7), "Cocina");
        Place(ground, "Dining Table 6 Seats", 4.2, 9.6);
        foreach (var (x, y, turn) in new[] { (3.6, 9.0, faceNorth), (4.8, 9.0, faceNorth), (3.6, 10.2, 0.0), (4.8, 10.2, 0.0) })
            Place(ground, "Dining Chair", x, y, turn);
        Place(ground, "Area Rug", 4.3, 3.6);
        // Indoor plants: detailed families when supplied, otherwise the catalog pot plant.
        void Houseplant(Guid level, string family, double scale, double x, double y)
        {
            if (load(family) is Guid id) session.PlaceComponentFamily(level, id, new Point2(x, y), 0.6, scale);
            else Place(level, "Indoor Plant", x, y);
        }
        Houseplant(ground, PachiraFamily, 1.3, 2.5, 11.2);
        Houseplant(ground, CalatheaFamily, 1.4, 5.8, 5.2);
        Place(ground, "Toilet", 11.4, 7.35);
        Place(ground, "Pedestal Sink", 12.3, 7.5);
        Place(ground, "Washing Machine", 13.4, 7.4);
        Place(ground, "Clothes Dryer", 14.2, 7.4);
        Place(ground, "Water Heater", 14.5, 5.7, faceWest);
        foreach (var (name, x) in new[] { ("Refrigerator", 11.4), ("Base Cabinet", 12.3), ("Kitchen Sink Cabinet", 13.25),
                     ("Cooking Range", 14.4) })
            Place(ground, name, x, 11.45);
        Place(ground, "Kitchen Island", 12.9, 9.6);
        foreach (var x in new[] { 12.3, 13.0, 13.7 }) Place(ground, "Bar Stool", x, 8.8);

        // Upper floor: the stairwell opens through the slab and a glass guard runs round it.
        session.AddStructuralElement(upper, StructuralElementKind.Opening, "Hueco de escalera", new Point2(9.6, 6.0), new Point2(9.6, 10.8));
        var guard = Element("Glass Guard 1100 mm");
        session.AddElementFromPreset(upper, guard, new Point2(8.95, 6.0), new Point2(8.95, 10.8));
        session.AddElementFromPreset(upper, guard, new Point2(10.25, 6.0), new Point2(10.25, 10.8));
        session.AddElementFromPreset(upper, guard, new Point2(9.0, 5.95), new Point2(10.2, 5.95));
        Door(Partition(upper, 0.15, 5.0, 12.8, 5.0), 7.5);
        var corridor = Partition(upper, 0.15, 6.5, 8.9, 6.5);
        Door(corridor, 3.3);
        Door(corridor, 7.5, 0.8);
        Door(Partition(upper, 10.3, 6.5, 15.05, 6.5), 2.2);
        Partition(upper, 6.5, 6.5, 6.5, 11.85);
        Partition(upper, 8.9, 6.5, 8.9, 11.85);
        Partition(upper, 10.3, 6.5, 10.3, 11.85);
        var upperBack = Existing(upper, 0, 11.925, 15.2, 11.925);
        Window(upperBack, 3.3, 2.4, 1.5, 0.9);
        Window(upperBack, 7.7, 0.6, 0.4, 1.6);
        Window(upperBack, 12.7, 2.0, 1.4, 0.9);
        Window(Existing(upper, 0.075, 0, 0.075, 12), 2.5, 2.4, 2.4, 0.1);
        Window(Existing(upper, 15.125, 0, 15.125, 12), 9.2, 1.2, 1.2, 0.9);
        session.AddRoom(upper, new Point2(0.3, 0.3), new Point2(12.7, 4.9), "Estudio");
        session.AddRoom(upper, new Point2(0.3, 5.1), new Point2(15.0, 6.4), "Pasillo");
        session.AddRoom(upper, new Point2(0.3, 6.6), new Point2(6.4, 11.8), "Dormitorio principal");
        session.AddRoom(upper, new Point2(6.6, 6.6), new Point2(8.8, 11.8), "Baño");
        session.AddRoom(upper, new Point2(10.4, 6.6), new Point2(15.0, 11.8), "Dormitorio 2");
        Place(upper, "Desk", 2.0, 1.0);
        Place(upper, "Office Chair", 2.0, 1.7, faceNorth);
        Place(upper, "Bookshelf", 4.5, 4.65, faceNorth);
        Place(upper, "Two Seat Sofa", 8.5, 4.4, faceNorth);
        Place(upper, "King Bed", 3.3, 10.75);
        foreach (var x in new[] { 1.95, 4.65 }) Place(upper, "Nightstand", x, 11.5);
        Place(upper, "Wardrobe", 3.3, 6.95, faceNorth);
        Place(upper, "Toilet", 7.2, 11.4);
        Place(upper, "Bathroom Vanity", 8.2, 11.5);
        Place(upper, "Shower Enclosure", 7.3, 7.1, faceNorth);
        Place(upper, "Double Bed", 12.7, 10.7);
        Place(upper, "Dresser", 14.6, 8.4, faceWest);
        Houseplant(upper, AnthuriumFamily, 1.0, 0.8, 11.3);
        Houseplant(upper, CalatheaFamily, 1.2, 12.2, 0.8);

        // Water from the meter at the street, sewage to the outlet at the back of the lot.
        session.AddPlumbingPoint(ground, PlumbingPointKind.WaterMeter, "Medidor", new Point2(16.5, -5.0));
        session.AddPlumbingPoint(ground, PlumbingPointKind.SewerOutlet, "Descarga", new Point2(16.5, 13.0));
        session.ConnectPlumbing((kind, number) => $"{kind} {number}");
    }
}
