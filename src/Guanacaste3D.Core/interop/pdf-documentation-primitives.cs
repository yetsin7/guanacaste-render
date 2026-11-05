using System.Globalization;

namespace Guanacaste3D.Core.Interop;

public static partial class PdfPlanExporter
{
    /// <summary>Includes associative measurements, notes, datums, section references, and families in printed plans.</summary>
    private static void AddDocumentationPrimitives(List<SheetPrimitive> items, BuildingDocument document, Guid levelId)
    {
        foreach (var axis in document.Grids)
        {
            items.Add(new SheetPolyline([axis.Start, axis.End], 0.13, Grey, Dashed: true));
            foreach (var point in new[] { axis.Start, axis.End })
            {
                items.Add(new SheetPolygon(Circle(point, 0.18, 32), 0.18, Black, (1, 1, 1)) { PaperRadius = 3 });
                items.Add(new SheetText(point, axis.Name, 2.5, Black) { CenteredOnSymbol = true });
            }
        }
        foreach (var dimension in document.Dimensions.Where(dimension => dimension.LevelId == levelId))
        {
            if (PlanDimensions.Resolve(document, dimension) is not { } measurement) continue;
            items.Add(new SheetPolyline([measurement.From, measurement.To], 0.13, Black));
            foreach (var point in new[] { measurement.From, measurement.To })
                items.Add(new SheetPolyline([new(point.X - 0.07, point.Y - 0.07),
                    new(point.X + 0.07, point.Y + 0.07)], 0.25, Black));
            items.Add(new SheetText(new((measurement.From.X + measurement.To.X) / 2,
                (measurement.From.Y + measurement.To.Y) / 2 + 0.08),
                measurement.IsAngle ? measurement.Value.ToString("0.0", CultureInfo.CurrentCulture) + "°" : document.Units.FormatLength(measurement.Value), 2.5, Black));
        }
        foreach (var element in document.StructuralElements.Where(element => element.LevelId == levelId))
        {
            if (element.Kind == StructuralElementKind.ModelText)
                items.Add(new SheetText(element.Start, element.Name, 2.5, Black));
            else if (element.Kind is StructuralElementKind.FilledRegion or StructuralElementKind.RevisionCloud)
                items.Add(new SheetPolygon(element.Footprint, 0.18, Black,
                    element.Kind == StructuralElementKind.FilledRegion ? (0.85, 0.85, 0.85) : null));
            else if (element.Kind == StructuralElementKind.ViewReference)
                items.Add(new SheetText(element.Start, element.Name, 2.5, Black));
            else if (element.Kind is StructuralElementKind.SpotElevation or StructuralElementKind.SpotCoordinate)
                items.Add(new SheetText(element.Start, "+ " + SpotMarks.Label(document, element), 2.5, Black));
            else if (element.Kind == StructuralElementKind.SpotSlope)
            {
                items.Add(new SheetPolyline([element.Start, element.End], 0.18, Black));
                items.Add(new SheetText(new((element.Start.X + element.End.X) / 2, (element.Start.Y + element.End.Y) / 2), SpotMarks.Label(document, element), 2.5, Black));
            }
            else if (element.Kind == StructuralElementKind.Keynote)
                items.Add(new SheetText(element.Start, "▸ " + element.Name, 2.5, Black));
            else if (element.Kind == StructuralElementKind.Symbol)
                items.Add(new SheetPolygon(Circle(element.Start, Math.Max(0.1, element.Width / 2), 24), 0.18, Black, null));
            else if (element.Kind == StructuralElementKind.Insulation)
                items.Add(new SheetPolygon(element.Footprint, 0.13, Grey, null));
            else if (element.IsDatumLine)
                items.Add(new SheetPolyline([element.Start, element.End], 0.13, Grey,
                    Dashed: element.Kind is StructuralElementKind.ReferencePlane or StructuralElementKind.Matchline));
        }
        foreach (var view in document.Views.Where(view => view.Cut is not null))
        {
            items.Add(new SheetPolyline([view.Cut!.Start, view.Cut.End], 0.18, Grey, Dashed: true));
            items.Add(new SheetText(view.Cut.Start, view.Name, 2.5, Black, Bold: true));
        }
        // Door, window and column marks (P1, V2, C1) in round tags, as a printed plan keys them to its schedules.
        foreach (var tag in ElementTags.Build(document, levelId,
                     new HashSet<TagCategory> { TagCategory.Door, TagCategory.Window, TagCategory.Column }))
        {
            items.Add(new SheetPolygon(Circle(tag.Position, 0.2, 24), 0.15, Black, (1, 1, 1)) { PaperRadius = 2.4 });
            items.Add(new SheetText(tag.Position, tag.Mark, 2.0, Black) { CenteredOnSymbol = true });
        }
        // Callouts: the dashed boundary of each enlarged detail and its name.
        foreach (var callout in (document.Callouts ?? []).Where(callout => callout.LevelId == levelId))
        {
            var region = callout.Region;
            items.Add(new SheetPolyline([new(region.MinX, region.MinY), new(region.MaxX, region.MinY), new(region.MaxX, region.MaxY),
                new(region.MinX, region.MaxY), new(region.MinX, region.MinY)], 0.18, Grey, Dashed: true));
            items.Add(new SheetText(new(region.MinX, region.MaxY + 0.15), callout.Name, 2.5, Black, Bold: true));
        }
        var elevation = document.Levels.Single(level => level.Id == levelId).Elevation;
        var upper = document.Levels.Where(level => level.Elevation > elevation).Select(level => level.Elevation)
            .DefaultIfEmpty(elevation + 3).Min();
        foreach (var family in document.InPlaceFamilies)
            foreach (var segment in FamilyPlanGeometry.ForLevel(family, elevation, upper))
                items.Add(new SheetPolyline([segment.Start, segment.End], 0.18, Black));
    }
}
