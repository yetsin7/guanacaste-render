using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the breeze-block house sample and the families and materials it introduces.</summary>
static class BreezeBlockHouseChecks
{
    public static void Run(Action<bool, string> check)
    {
        var document = BreezeBlockHouseShowcase.Create();
        document.Validate();
        var path = Path.Combine(Path.GetTempPath(), $"cobogo-{Guid.NewGuid():N}.g3");
        ProjectFile.Save(path, document);
        ProjectFile.Load(path).Validate();
        File.Delete(path);
        check(true, "Breeze-block house validates and survives a save and reload");

        var screen = document.StructuralElements.Single(e => e.Style == ElementStyle.BreezeBlock);
        var cells = ElementGeometry.Parts(screen).ToArray();
        check(screen.Kind == StructuralElementKind.CurtainWall && cells.Length > 20 && cells.All(part => part.Kind == SurfaceKind.Wall) &&
              cells.All(part => part.Kind != SurfaceKind.Glass),
            "The cobogó is a curtain wall of open masonry webs without glass");

        var cars = document.StructuralElements.Where(e => e.ComponentShape is ResidentialComponentShape.Car or ResidentialComponentShape.SilverCar).ToArray();
        check(cars.Length == 2 && cars.All(car => car.Depth > 4), "Two full-size cars are parked in the carport");
        var carParts = cars.SelectMany(ElementGeometry.Parts).ToArray();
        check(new[] { SurfaceKind.CarPaint, SurfaceKind.CarPaintSilver, SurfaceKind.Rubber, SurfaceKind.Glass }
                .All(kind => carParts.Any(part => part.Kind == kind)) &&
              carParts.All(part => part.Top <= cars[0].Height + 1e-9),
            "Cars have painted bodies, tyres and glass within their family height");

        check(document.Openings.Any(opening => opening.Kind == OpeningKind.Door && opening.Height > 2.7 &&
                document.OpeningTypes.Single(type => type.Id == opening.OpeningTypeId).FamilyName == "Pivot Door"),
            "The entrance is a tall timber pivot door");
        check(document.Rooms.Count >= 12 && document.Openings.Count(opening => opening.Kind == OpeningKind.Door) >= 8 &&
              document.Openings.Count(opening => opening.Kind == OpeningKind.Window) >= 8 &&
              document.StructuralElements.Any(element => element.Kind == StructuralElementKind.Opening) &&
              document.PlumbingSystems.Count > 0,
            "The house has its rooms, doors, windows, stairwell and plumbing");
        Drawings(check, document);

        var mesh = BuildingMesher.Build(document, includeGround: true);
        foreach (var kind in new[] { SurfaceKind.Asphalt, SurfaceKind.WallConcrete, SurfaceKind.WallWood, SurfaceKind.Ground,
                     SurfaceKind.Glass, SurfaceKind.CarPaint })
            check(mesh.TriangleKinds.Contains(kind), $"The house meshes {kind} surfaces");
        check(MaterialLibrary.ForKind(SurfaceKind.CarPaint).Roughness < 0.15f &&
              MaterialLibrary.ForKind(SurfaceKind.Asphalt).BaseColor.X < 0.2f,
            "Car paint is glossy and asphalt is dark");

        var canopy = document.Floors.Single(f => f.Finish == FinishKind.ExposedConcrete && f.ElevationOffset > 3.5);
        var volume = document.Floors.Where(f => f.Finish == FinishKind.Paint).ToArray();
        check(volume.Length >= 2 && canopy.Boundary.Min(p => p.Y) < volume.Min(f => f.Boundary.Min(p => p.Y)),
            "A fair-faced canopy overhangs the painted white volume toward the street");
        check(BudgetCalculator.Build(document, UnitPriceList.Default()).Total > 0, "The house can be priced");

        // The street view of the photograph travels with the project and is checked like any other data.
        check(document.RenderSetup == BreezeBlockHouseShowcase.StreetView && document.Clone().RenderSetup == document.RenderSetup &&
              ProjectFileRoundTrip(document).RenderSetup == document.RenderSetup,
            "The house keeps its render camera, sun and filters through clone and save");
        var sun = SolarPosition.Direction(document.RenderSetup!.Latitude, document.RenderSetup.DayOfYear, document.RenderSetup.SolarHour);
        check(sun.X < 0 && sun.Y < 0 && SolarPosition.AltitudeDegrees(sun) is > 25 and < 45,
            "The saved sun shines from the front left, so the tree's shadow falls across the white volume");
        var broken = document.Clone();
        broken.RenderSetup = broken.RenderSetup! with { FieldOfViewDegrees = 0 };
        var rejected = false;
        try { broken.Validate(); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "An invalid saved render setup is rejected");
    }

    /// <summary>The document set: grid, dimensions, sections, schedules, cropped views and six composed sheets.</summary>
    private static void Drawings(Action<bool, string> check, BuildingDocument document)
    {
        check(document.Grids.Count == 7 && document.Dimensions.Count > 10 &&
              document.Views.Count(view => view.Kind == ProjectViewKind.Section) == 2 && document.Schedules.Count == 3,
            "The house has its grid, dimensions, two sections and three schedules");
        check(document.Sheets.Select(sheet => sheet.Name).SequenceEqual(["Portada", "Planta baja", "Planta alta", "Alzados", "Secciones", "Tablas"]),
            "The drawing set has a cover, both plans, elevations, sections and schedules");
        var tables = document.Sheets.Single(sheet => sheet.Name == "Tablas");
        check(tables.Viewports.Count(viewport => document.Schedules.Any(schedule => schedule.Id == viewport.ViewId)) == 3,
            "Schedules are placed on a sheet as tables");
        var composed = SheetComposition.Build(document, tables, DateTimeOffset.Now);
        var texts = composed.Viewports.SelectMany(viewport => viewport.Graphics).OfType<Guanacaste3D.Core.Interop.SheetText>()
            .Select(text => text.Text).ToArray();
        check(texts.Contains("TABLA DE PUERTAS") && texts.Contains("P1") && texts.Contains("V1") && !texts.Any(text => text.Length == 8 &&
                text.All(Uri.IsHexDigit)),
            "Door and window schedules list type marks (P1, V1), not identifiers");

        var plan = document.Sheets.Single(sheet => sheet.Name == "Planta baja");
        var drawn = SheetComposition.Build(document, plan, DateTimeOffset.Now).Viewports.Single();
        var crop = document.Views.Single(view => view.Id == plan.Viewports[0].ViewId).ModelCrop!.Value;
        check(drawn.Scale == 50 && drawn.Graphics.OfType<Guanacaste3D.Core.Interop.SheetText>().Any(text => text.Text == "P1") &&
              drawn.Graphics.OfType<Guanacaste3D.Core.Interop.SheetText>().Any(text => text.Text == "Cocina"),
            "The ground floor sheet prints at 1:50 with door tags and room names");
        var paper = drawn.Graphics.SelectMany(primitive => primitive switch
        {
            Guanacaste3D.Core.Interop.SheetPolyline line => line.Points,
            Guanacaste3D.Core.Interop.SheetPolygon polygon when polygon.PaperRadius is null => polygon.Points,
            _ => []
        }).ToArray();
        var expected = (crop.MaxX - crop.MinX) * 1000 / 50;
        check(paper.Max(point => point.X) - paper.Min(point => point.X) <= expected + 0.5,
            "A cropped plan prints only its crop region, not the street and gardens around it");

        var clipped = SheetPrimitiveClipper.ClipPolygon([new(-5, -5), new(5, -5), new(5, 5), new(-5, 5)], new PlanBounds(0, 0, 10, 10));
        check(clipped.Count == 4 && clipped.All(point => point.X is >= 0 and <= 5 && point.Y is >= 0 and <= 5) &&
              SheetPrimitiveClipper.ClipSegment(new(-1, 20), new(11, 20), new PlanBounds(0, 0, 10, 10)) is null,
            "Crop clipping trims shapes to the region and drops lines outside it");
    }

    private static BuildingDocument ProjectFileRoundTrip(BuildingDocument document)
    {
        var path = Path.Combine(Path.GetTempPath(), $"render-setup-{Guid.NewGuid():N}.g3");
        try
        {
            ProjectFile.Save(path, document);
            return ProjectFile.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
