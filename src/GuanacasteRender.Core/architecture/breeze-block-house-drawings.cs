namespace Guanacaste3D.Core;

public static partial class BreezeBlockHouseShowcase
{
    /// <summary>
    /// The construction document set: structural grid, associative dimensions, two sections, door, window and room
    /// schedules, cropped views and six A1 sheets (cover, ground floor, upper floor, elevations, sections, schedules).
    /// Door, window and column tags, room names and areas print automatically on the plans.
    /// </summary>
    private static void AddDrawingSet(DocumentSession session, Guid ground, Guid upper)
    {
        foreach (var (name, x) in new[] { ("A", 0.15), ("B", 6.55), ("C", 10.8), ("D", 15.05) })
            session.AddGridAxis(name, new Point2(x, -2.2), new Point2(x, 13.6));
        foreach (var (name, y) in new[] { ("1", 0.4), ("2", 5.2), ("3", 11.85) })
            session.AddGridAxis(name, new Point2(-1.6, y), new Point2(16.6, y));
        session.AutoDimensionLevel(ground);
        session.AutoDimensionLevel(upper);

        // Sections: A-A across the living room, carport and breeze-block balcony; B-B along the stair.
        var views = session.Document.Views;
        session.RenameView(BuiltInProjectViews.SectionAId, "Sección A-A");
        session.UpdateSectionCut(BuiltInProjectViews.SectionAId, new SectionCut(new Point2(-1.5, 2.8), new Point2(16.7, 2.8)));
        var sectionB = session.AddSectionView("Sección B-B", new SectionCut(new Point2(9.6, -2.5), new Point2(9.6, 13.0)));

        Guid Plan(Guid level) => views.Single(view => view.Kind == ProjectViewKind.Plan && view.LevelId == level).Id;
        var groundPlan = Plan(ground);
        var upperPlan = Plan(upper);
        session.SetViewModelCrop(groundPlan, new PlanBounds(-2.6, -8.2, 17.6, 14.4));
        session.SetViewModelCrop(upperPlan, new PlanBounds(-2.0, -2.6, 17.2, 14.0));
        // Elevations print the house, its tree and a strip of ground; the streets and neighbours stay out.
        session.SetViewModelCrop(BuiltInProjectViews.SouthElevationId, new PlanBounds(-18.5, -0.6, 2.5, 14.5));
        session.SetViewModelCrop(BuiltInProjectViews.NorthElevationId, new PlanBounds(-2.5, -0.6, 18.5, 14.5));
        session.SetViewModelCrop(BuiltInProjectViews.EastElevationId, new PlanBounds(-6.5, -0.6, 14.5, 14.5));
        session.SetViewModelCrop(BuiltInProjectViews.WestElevationId, new PlanBounds(-14.5, -0.6, 6.5, 14.5));
        session.SetViewModelCrop(BuiltInProjectViews.SectionAId, new PlanBounds(0, -0.6, 18.2, 8.5));
        session.SetViewModelCrop(sectionB, new PlanBounds(0, -0.6, 15.5, 8.5));
        // The cover's isometric keeps the house and its tree; the street and the neighbours fall outside.
        session.SetViewModelCrop(BuiltInProjectViews.Model3DId, new PlanBounds(-12, -4.5, 16.5, 24));

        var doors = session.AddSchedule("Tabla de puertas", ElementCategory.Doors);
        var windows = session.AddSchedule("Tabla de ventanas", ElementCategory.Windows);
        var rooms = session.AddSchedule("Tabla de acabados y habitaciones", ElementCategory.Rooms);

        // The first sheet replaces the default one; each sheet arranges its views as fractions of the drawing area.
        foreach (var sheet in session.Document.Sheets.ToArray()) session.RemoveSheet(sheet.Id);
        void Sheet(string name, string description, params (Guid View, double Left, double Top, double Width, double Height, double Scale)[] placed)
        {
            var id = session.AddSheet(SheetPaperSize.A1, name);
            var sheet = session.Document.Sheets.Single(candidate => candidate.Id == id);
            var area = sheet.Viewports[0];
            session.UpdateSheet(sheet with
            {
                Subtitle = "CASA DEL COBOGÓ", Description = description, DateText = "Octubre 2026",
                Viewports = placed.Select((view, index) => new SheetViewport(Guid.NewGuid(), view.View,
                    area.X + view.Left * area.Width, area.Y + view.Top * area.Height, view.Width * area.Width,
                    view.Height * area.Height, view.Scale, (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList()
            });
        }
        Sheet("Portada", "Vista general, localización y cuadro de áreas.",
            (BuiltInProjectViews.Model3DId, 0, 0, 1, 0.55, 0),
            (BuiltInProjectViews.LocationId, 0, 0.58, 0.55, 0.42, 0),
            (BuiltInProjectViews.AreaScheduleId, 0.58, 0.58, 0.42, 0.42, 0));
        Sheet("Planta baja", "Planta arquitectónica de planta baja con cotas, ejes y etiquetas.", (groundPlan, 0, 0, 1, 1, 50));
        Sheet("Planta alta", "Planta arquitectónica de planta alta con cotas, ejes y etiquetas.", (upperPlan, 0, 0, 1, 1, 50));
        Sheet("Alzados", "Alzados sur (calle), norte, este y oeste.",
            (BuiltInProjectViews.SouthElevationId, 0, 0, 1, 0.25, 100),
            (BuiltInProjectViews.NorthElevationId, 0, 0.25, 1, 0.25, 100),
            (BuiltInProjectViews.EastElevationId, 0, 0.5, 1, 0.25, 100),
            (BuiltInProjectViews.WestElevationId, 0, 0.75, 1, 0.25, 100));
        Sheet("Secciones", "Sección transversal A-A y longitudinal B-B.",
            (BuiltInProjectViews.SectionAId, 0, 0, 1, 0.5, 50),
            (sectionB, 0, 0.5, 1, 0.5, 50));
        Sheet("Tablas", "Tablas de puertas, ventanas, habitaciones y áreas.",
            (doors, 0, 0, 1, 0.25, 0),
            (windows, 0, 0.26, 1, 0.25, 0),
            (rooms, 0, 0.52, 1, 0.3, 0),
            (BuiltInProjectViews.AreaScheduleId, 0, 0.83, 0.6, 0.17, 0));
    }
}
