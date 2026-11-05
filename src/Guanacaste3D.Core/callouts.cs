namespace Guanacaste3D.Core;

/// <summary>
/// A named detail region marked on a level's plan. It keeps the rectangle in model coordinates, so it follows the
/// project when saved, and lets the plan zoom straight to the area being detailed.
/// </summary>
public sealed record Callout(Guid Id, Guid LevelId, string Name, PlanBounds Region);

/// <summary>Rules for callouts.</summary>
public static class CalloutRules
{
    /// <summary>Smallest side of a callout region, in meters.</summary>
    public const double MinimumSide = 0.5;

    /// <summary>Rejects callouts on missing levels, with empty regions, or with duplicate identities.</summary>
    public static void Validate(BuildingDocument document)
    {
        var callouts = document.Callouts ?? [];
        if (callouts.Count > 10000 || callouts.Any(callout => callout is null) ||
            callouts.Select(callout => callout.Id).Distinct().Count() != callouts.Count)
            throw new InvalidDataException("Invalid callout.");
        foreach (var callout in callouts)
        {
            var region = callout.Region;
            var finite = double.IsFinite(region.MinX) && double.IsFinite(region.MinY) &&
                double.IsFinite(region.MaxX) && double.IsFinite(region.MaxY);
            if (callout.Id == Guid.Empty || string.IsNullOrWhiteSpace(callout.Name) || callout.Name.Length > 120 || !finite ||
                region.MaxX - region.MinX < MinimumSide || region.MaxY - region.MinY < MinimumSide ||
                document.Levels.All(level => level.Id != callout.LevelId))
                throw new InvalidDataException("Invalid callout.");
        }
    }
}

public sealed partial class DocumentSession
{
    /// <summary>Marks a detail region on a level's plan. Returns the new callout's id.</summary>
    public Guid AddCallout(Guid levelId, string name, PlanBounds region)
    {
        var id = Guid.NewGuid();
        var trimmed = name.Trim();
        if (trimmed.Length == 0) trimmed = $"Detalle {Document.Callouts.Count(callout => callout.LevelId == levelId) + 1}";
        var normalized = new PlanBounds(Math.Min(region.MinX, region.MaxX), Math.Min(region.MinY, region.MaxY),
            Math.Max(region.MinX, region.MaxX), Math.Max(region.MinY, region.MaxY));
        Commit("Transaction.AddCallout", document => document.Callouts.Add(new Callout(id, levelId, trimmed, normalized)));
        return id;
    }

    /// <summary>Marks a detail region around an element's plan outline, grown by a margin in meters.</summary>
    public Guid AddCalloutAround(Guid elementId, double margin = 1.0, string? name = null)
    {
        if (PlanDimensions.Bounds(Document, elementId) is not { } owner)
            throw new InvalidDataException("The element has no plan outline to put a callout around.");
        var b = owner.Bounds;
        var side = Math.Max(0, margin);
        var region = new PlanBounds(b.MinX - side, b.MinY - side, b.MaxX + side, b.MaxY + side);
        // A very small element still gets a usable region.
        if (region.MaxX - region.MinX < CalloutRules.MinimumSide || region.MaxY - region.MinY < CalloutRules.MinimumSide)
        {
            var (cx, cy) = ((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
            region = new PlanBounds(cx - CalloutRules.MinimumSide, cy - CalloutRules.MinimumSide,
                cx + CalloutRules.MinimumSide, cy + CalloutRules.MinimumSide);
        }
        return AddCallout(owner.LevelId, name ?? string.Empty, region);
    }

    /// <summary>Removes a callout. The model and views are untouched.</summary>
    public void RemoveCallout(Guid id)
    {
        if (Document.Callouts.All(callout => callout.Id != id)) return;
        Commit("Transaction.RemoveCallout", document => document.Callouts.RemoveAll(callout => callout.Id == id));
    }

    /// <summary>Deletes a saved view template. Views that already took it on keep their visibility.</summary>
    public void RemoveViewTemplate(Guid templateId)
    {
        if (Document.ViewTemplates.All(template => template.Id != templateId)) return;
        Commit("Transaction.RemoveViewTemplate", document => document.ViewTemplates.RemoveAll(template => template.Id == templateId));
    }
}
