using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks the native toilet's geometry, file representation, and project plumbing association.</summary>
static class ToiletFamilyChecks
{
    /// <summary>Verifies persistence and coordinated geometry and plumbing for the residential fixture.</summary>
    public static void Run(Action<bool, string> check)
    {
        var house = TwoStoreyHouse.Create();
        var toilet = house.StructuralElements.Single(element => element.ComponentShape == ResidentialComponentShape.Toilet);
        var parts = ResidentialComponentGeometry.Parts(toilet).ToArray();
        var bowlCenter = new Point2(toilet.Start.X, toilet.Start.Y - toilet.Depth * 0.08);
        check(parts.Count(part => part.Kind == SurfaceKind.Ceramic) > 20 &&
              !parts.Where(part => part.Bottom > 0.38 && part.Bottom < 0.44)
                  .Any(part => SlabOpenings.ContainsPoint(part.Footprint, bowlCenter)),
            "The toilet has an open bowl and detailed ceramic geometry");
        var rotated = toilet with { Rotation = Math.PI / 2 };
        var expected = new Point2(toilet.Start.X - (toilet.Depth / 2 - 0.06), toilet.Start.Y);
        check(PlumbingStandards.Connection(rotated).DistanceTo(expected) < 1e-9 &&
              ResidentialComponentGeometry.Parts(rotated).Count() == parts.Length,
            "Rotating the toilet rotates its modeled water connection and all its parts");
        var analysis = PlumbingAnalysis.Analyze(house);
        check(new[] { PlumbingSystemKind.ColdWater, PlumbingSystemKind.Sanitary }.All(kind =>
                  analysis.Systems.Any(system => system.System.Kind == kind && system.Fixtures.Any(fixture => fixture.FixtureId == toilet.Id))) &&
              !analysis.Unserved.Any(item => item.Fixture.Id == toilet.Id),
            "The toilet in the house is associated with cold water and sanitary drainage");

        var path = Path.Combine(Path.GetTempPath(), $"toilet-{Guid.NewGuid():N}.g3df");
        try
        {
            var family = ToiletFamily.Create();
            NativeDocumentStore.SaveFamily(path, family);
            var reloaded = NativeDocumentStore.LoadFamily(path);
            check(reloaded.Category == FamilyCategory.PlumbingFixtures && reloaded.Operations.Count == parts.Length &&
                  FamilyGeometry.Tessellate(reloaded).TriangleCount > 0 && reloaded.Metadata.ContainsKey("ColdWaterInlet"),
                "The toilet family preserves its editable extrusions and connection description in the native container");
        }
        finally { File.Delete(path); }
    }
}
