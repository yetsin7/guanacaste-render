using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks the built-in home component catalog, geometry, plan footprint, and native persistence.</summary>
static class ResidentialComponentChecks
{
    /// <summary>Runs all residential component contract checks.</summary>
    public static void Run(Action<bool, string> check)
    {
        var presets = ResidentialComponentCatalog.Presets;
        check(presets.Count >= 35 && presets.Select(preset => preset.Name).Distinct().Count() == presets.Count &&
              presets.All(preset => preset.Kind == StructuralElementKind.Component && preset.Depth > 0 &&
                  preset.ComponentCategory != ResidentialComponentCategory.None &&
                  !string.IsNullOrWhiteSpace(preset.LocalizationKey)),
            "The built-in residential catalog has 35+ distinct, localizable, ready-to-place component families");
        check(Enum.GetValues<ResidentialComponentCategory>().Where(category => category != ResidentialComponentCategory.None)
                .All(category => presets.Any(preset => preset.ComponentCategory == category)),
            "The residential catalog covers every home category");

        var session = new DocumentSession(BuildingDocument.Create("Residential components"));
        var level = session.Document.Levels[0].Id;
        for (var index = 0; index < presets.Count; index++)
        {
            var preset = presets[index];
            var position = new Point2(index % 8 * 3, index / 8 * 4);
            session.AddElementFromPreset(level, preset, position, position);
        }
        session.Document.Validate();
        var elements = session.Document.StructuralElements;
        check(elements.Count == presets.Count && elements.All(element => ElementGeometry.Parts(element).Any()),
            "Every residential family produces coordinated solid geometry");
        check(elements.Zip(presets).All(pair => HasExpectedFootprint(pair.First, pair.Second)),
            "Residential family footprints preserve independent width and depth");

        var mesh = BuildingMesher.Build(session.Document);
        check(mesh.TriangleCount > presets.Count * 20,
            "Residential families participate in the shared 3D mesh with detailed silhouettes");
        var reloaded = ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(session.Document));
        check(reloaded.StructuralElements.Select(element => (element.Depth, element.ComponentShape, element.ComponentCategory))
                .SequenceEqual(elements.Select(element => (element.Depth, element.ComponentShape, element.ComponentCategory))),
            "Residential family type and dimensions persist in native project documents");

        check(Enum.GetValues<ResidentialComponentShape>().Where(shape => shape != ResidentialComponentShape.Box)
                .All(shape => presets.Any(preset => preset.ComponentShape == shape)) &&
              presets.Count(preset => preset.ComponentCategory == ResidentialComponentCategory.Bathroom) >= 9 &&
              presets.Count(preset => preset.ComponentCategory == ResidentialComponentCategory.Kitchen) >= 7,
            "Every family shape is in the catalog, with full bathroom and kitchen ranges");
        var sink = elements.Single(element => element.ComponentShape == ResidentialComponentShape.KitchenSink);
        var sinkParts = ElementGeometry.Parts(sink).ToArray();
        check(sinkParts.Any(part => part.Kind == SurfaceKind.Metal && part.Top < sink.Offset + sink.Height - 0.1),
            "The kitchen sink has a steel bowl below the counter");
        check(elements.Any(element => ElementGeometry.Parts(element).Any(part => part.Kind == SurfaceKind.Mirror)),
            "Mirrors are silvered glass");
        StructuralElement Placed(string name) => elements[presets.Select(preset => preset.Name).ToList().IndexOf(name)];
        var doubleVanity = Placed("Double Vanity");
        check(ElementGeometry.Parts(doubleVanity).Count(part => part.Kind == SurfaceKind.Metal) >=
              2 * ElementGeometry.Parts(Placed("Bathroom Vanity")).Count(part => part.Kind == SurfaceKind.Metal),
            "A double vanity has two basins with their mixers");

        // Rotation: SPACE or Rotate turns a component about its insertion point.
        var sofa = Placed("Three Seat Sofa");
        session.RotateElement(sofa.Id, Math.PI / 2);
        var turned = session.Document.StructuralElements.Single(element => element.Id == sofa.Id);
        double Extent(IEnumerable<Point2> points, Func<Point2, double> axis) => points.Max(axis) - points.Min(axis);
        var turnedParts = ElementGeometry.Parts(turned).SelectMany(part => part.Footprint).ToArray();
        var originalParts = ElementGeometry.Parts(sofa).SelectMany(part => part.Footprint).ToArray();
        check(Math.Abs(turned.Rotation - Math.PI / 2) < 1e-12 &&
              Math.Abs(Extent(turnedParts, point => point.X) - Extent(originalParts, point => point.Y)) < 1e-6 &&
              Math.Abs(Extent(turned.Footprint, point => point.Y) - sofa.Width) < 1e-9,
            "A quarter turn swaps a component's plan width and depth, solids and footprint alike");
        // Front was -Y; a quarter turn counter-clockwise points it toward +X, so the back cushions sit toward -X.
        var back = ElementGeometry.Parts(turned).Where(part => part.Bottom > turned.Offset + 0.45)
            .SelectMany(part => part.Footprint).Average(point => point.X);
        check(back < turned.Start.X, "The turned sofa faces the new direction");
        for (var turn = 0; turn < 3; turn++) session.RotateElement(sofa.Id, Math.PI / 2);
        check(session.Document.StructuralElements.Single(element => element.Id == sofa.Id).Rotation == 0,
            "Four quarter turns bring a component exactly back");
        session.Undo();
        var roundTrip = ProjectFile.DeserializeDocument(ProjectFile.SerializeDocument(session.Document));
        check(Math.Abs(roundTrip.StructuralElements.Single(element => element.Id == sofa.Id).Rotation - 3 * Math.PI / 2) < 1e-12,
            "Component rotation is undoable and persists in the project");
        var wall = session.AddStructuralElement(level, StructuralElementKind.Beam, "Viga", new Point2(0, -5), new Point2(4, -5));
        try
        {
            session.RotateElement(wall, Math.PI / 2);
            check(false, "Line elements do not turn about a point");
        }
        catch (InvalidDataException) { check(true, "Line elements do not turn about a point"); }
    }

    /// <summary>Tests the independent width and depth of a placed component's rectangular plan footprint.</summary>
    private static bool HasExpectedFootprint(StructuralElement element, ElementPreset preset) =>
        Math.Abs((element.Footprint.Max(point => point.X) - element.Footprint.Min(point => point.X)) - preset.Width) < 1e-9 &&
        Math.Abs((element.Footprint.Max(point => point.Y) - element.Footprint.Min(point => point.Y)) - preset.Depth) < 1e-9;
}
