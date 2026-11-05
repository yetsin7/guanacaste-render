using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>Creates an editable extrusion-based native representation of the built-in residential toilet.</summary>
public static class ToiletFamily
{
    /// <summary>
    /// Captures the default catalog geometry as a standalone family. Connection information is descriptive metadata;
    /// project plumbing is currently associated with the built-in component rather than generic family connectors.
    /// </summary>
    public static FamilyDocument Create()
    {
        var preset = ResidentialComponentCatalog.Presets.Single(item => item.ComponentShape == ResidentialComponentShape.Toilet);
        var element = new StructuralElement(Guid.NewGuid(), Guid.NewGuid(), StructuralElementKind.Component,
            preset.Name, new(0, 0), new(0, 0), preset.Width, preset.Height, 0)
        {
            Depth = preset.Depth,
            ComponentShape = ResidentialComponentShape.Toilet
        };
        var plane = new FamilyWorkPlane(Guid.NewGuid(), "Floor plane", new(0, 0, 0), new(0, 0, 1), new(1, 0, 0));
        var parts = ResidentialComponentGeometry.Parts(element).ToArray();
        var family = new FamilyDocument
        {
            Name = "Residential toilet",
            Category = FamilyCategory.PlumbingFixtures,
            Types = [new FamilyTypeDefinition(Guid.NewGuid(), "Standard", [])],
            WorkPlanes = [plane],
            Metadata = new Dictionary<string, string>
            {
                ["ColdWaterInlet"] = "Rear center, 0.20 m above floor; nominal connection only",
                ["SanitaryOutlet"] = "Integral trap and floor outlet; nominal connection only",
                ["ConnectionSupport"] = "Generic family connectors are not yet supported"
            },
            Operations = parts.Select((part, index) => new FamilyOperation(Guid.NewGuid(), $"Toilet part {index + 1}",
                FamilyOperationKind.SolidExtrusion)
            {
                WorkPlaneId = plane.Id,
                Profile = [.. part.Footprint],
                ComponentRole = part.Kind.ToString(),
                Expressions = new Dictionary<string, string>
                {
                    [FamilyGeometry.DepthKey] = (part.Top - part.Bottom).ToString("R", CultureInfo.InvariantCulture),
                    [FamilyGeometry.OffsetKey] = part.Bottom.ToString("R", CultureInfo.InvariantCulture)
                }
            }).ToList()
        };
        family.Validate();
        return family;
    }
}
