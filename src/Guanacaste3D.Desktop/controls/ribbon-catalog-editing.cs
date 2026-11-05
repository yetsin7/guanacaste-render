namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Provides editing and extension workspace definitions for the professional ribbon catalog.
/// </summary>
public static partial class RibbonCatalog
{
    /// <summary>Creates a prominent command definition.</summary>
    private static RibbonCommandDefinition L(string id, string label, string icon) =>
        new(id, label, icon, RibbonCommandSize.Large);

    /// <summary>Creates a compact command definition.</summary>
    private static RibbonCommandDefinition S(string id, string label, string icon) =>
        new(id, label, icon, RibbonCommandSize.Compact);

    /// <summary>Creates a localized group from its ordered command definitions.</summary>
    private static RibbonGroupDefinition G(string label, params RibbonCommandDefinition[] commands) =>
        new(label, commands);

    /// <summary>Defines first-party extension entry points without claiming third-party availability.</summary>
    private static IReadOnlyList<RibbonGroupDefinition> AddIns() =>
    [
        Selection(),
        G("RibbonGroup.ModelTransfer",
            L("TransmitModel", "RibbonCommand.TransmitModel", "Icon.ExportCube"),
            S("Help", "RibbonCommand.Help", "Icon.Apply"),
            S("About", "Action.About", "Icon.Apply")),
        G("RibbonGroup.Conversion", L("FamilyConverter", "RibbonCommand.FamilyConverter", "Icon.ExportCube")),
        G("RibbonGroup.Automation", L("AiAssistant", "RibbonCommand.AutomationAssistant", "Icon.Render")),
    ];

    /// <summary>Defines contextual editing, geometry, transformation, measurement, and creation tools.</summary>
    private static IReadOnlyList<RibbonGroupDefinition> Modify() =>
    [
        Selection(),
        G("RibbonGroup.Properties", L("Properties", "RibbonCommand.Properties", "Icon.Parameters")),
        G("RibbonGroup.Clipboard",
            L("Paste", "RibbonCommand.Paste", "Icon.Paste"),
            S("Cut", "RibbonCommand.Cut", "Icon.Section"),
            S("Copy", "RibbonCommand.Copy", "Icon.Paste"),
            S("MatchProperties", "RibbonCommand.MatchProperties", "Icon.Apply")),
        G("RibbonGroup.Geometry",
            S("TrimExtend", "RibbonCommand.TrimExtend", "Icon.Section"),
            S("CutGeometry", "RibbonCommand.CutGeometry", "Icon.Section"),
            S("JoinGeometry", "RibbonCommand.JoinGeometry", "Icon.Section"),
            S("UnjoinGeometry", "RibbonCommand.UnjoinGeometry", "Icon.Section")),
        G("RibbonGroup.Transform",
            L("Move", "Action.Move", "Icon.Move"),
            S("CopyElement", "Action.Duplicate", "Icon.Paste"),
            S("CreateSimilar", "RibbonCommand.CreateSimilar", "Icon.Wall"),
            S("Rotate", "Action.Rotate", "Icon.Move"),
            S("MirrorPickAxis", "RibbonCommand.MirrorPickAxis", "Icon.Move"),
            S("Align", "RibbonCommand.Align", "Icon.Move"),
            S("Array", "RibbonCommand.Array", "Icon.Grid"),
            S("Scale", "RibbonCommand.Scale", "Icon.Fit"),
            S("Split", "RibbonCommand.Split", "Icon.Section"),
            S("Offset", "RibbonCommand.Offset", "Icon.Section"),
            S("Pin", "RibbonCommand.Pin", "Icon.Apply"),
            S("Delete", "Action.Delete", "Icon.Section")),
        G("RibbonGroup.Measure",
            L("Measure", "RibbonCommand.MeasureBetween", "Icon.Fit"),
            S("MeasureAlong", "RibbonCommand.MeasureAlong", "Icon.Fit")),
        G("RibbonGroup.Create",
            S("CreateGroup", "RibbonCommand.CreateGroup", "Icon.Cube"),
            S("CreateAssembly", "RibbonCommand.CreateAssembly", "Icon.Cube")),
    ];
}
