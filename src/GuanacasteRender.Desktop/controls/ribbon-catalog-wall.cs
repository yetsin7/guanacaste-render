namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Provides the contextual wall authoring workspace for the professional ribbon catalog.
/// </summary>
public static partial class RibbonCatalog
{
    /// <summary>Defines wall authoring, constraints, profiles, joins, and transformation tools.</summary>
    private static IReadOnlyList<RibbonGroupDefinition> Wall() =>
    [
        Selection(),
        G("RibbonGroup.WallProperties",
            L("WallProperties", "RibbonCommand.WallProperties", "Icon.WallEnhanced"),
            S("WallTypes", "RibbonCommand.WallTypes", "Icon.Parameters"),
            S("Levels", "Action.Levels", "Icon.Levels"),
            S("OpeningTypes", "Action.OpeningTypes", "Icon.OpeningTypes")),
        G("RibbonGroup.WallConstraints",
            L("AttachTopBase", "RibbonCommand.AttachTopBase", "Icon.Attach"),
            L("DetachTopBase", "RibbonCommand.DetachTopBase", "Icon.Detach"),
            S("AttachTop", "RibbonCommand.AttachTop", "Icon.Attach"),
            S("AttachBase", "RibbonCommand.AttachBase", "Icon.Attach"),
            S("DetachAll", "RibbonCommand.DetachAll", "Icon.Detach")),
        G("RibbonGroup.WallProfile",
            L("EditProfile", "RibbonCommand.EditProfile", "Icon.WallProfile"),
            S("ResetProfile", "RibbonCommand.ResetProfile", "Icon.WallProfile"),
            S("FlipWall", "RibbonCommand.FlipWall", "Icon.Flip"),
            S("WallOpenings", "RibbonCommand.WallOpenings", "Icon.OpeningTypes")),
        G("RibbonGroup.WallJoins",
            L("WallJoins", "RibbonCommand.WallJoins", "Icon.Join"),
            S("AllowJoin", "RibbonCommand.AllowJoin", "Icon.Join"),
            S("DisallowJoin", "RibbonCommand.DisallowJoin", "Icon.DisallowJoin"),
            S("MiterJoin", "RibbonCommand.MiterJoin", "Icon.Join"),
            S("ButtJoin", "RibbonCommand.ButtJoin", "Icon.Join"),
            S("SquareOff", "RibbonCommand.SquareOff", "Icon.Join")),
        G("RibbonGroup.Transform",
            L("Move", "Action.Move", "Icon.Move"),
            S("CopyElement", "Action.Duplicate", "Icon.Paste"),
            S("CreateSimilar", "RibbonCommand.CreateSimilar", "Icon.Wall"),
            S("Rotate", "Action.Rotate", "Icon.Rotate"),
            S("MirrorPickAxis", "RibbonCommand.MirrorPickAxis", "Icon.Flip"),
            S("Align", "RibbonCommand.Align", "Icon.Align"),
            S("Offset", "RibbonCommand.Offset", "Icon.Offset"),
            S("Array", "RibbonCommand.Array", "Icon.Grid"),
            S("Delete", "Action.Delete", "Icon.Delete")),
        G("RibbonGroup.WallView",
            S("Plan", "Tool.Plan", "Icon.Plan"),
            S("Elevation", "Tool.Elevation", "Icon.Elevation"),
            S("Section", "Tool.Section", "Icon.Section")),
    ];
}
