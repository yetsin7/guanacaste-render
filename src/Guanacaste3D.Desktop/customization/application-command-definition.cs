using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop.Customization;

/// <summary>
/// Describes an application command that may be assigned to quick access or a keyboard sequence.
/// </summary>
public sealed record ApplicationCommandDefinition(
    string CommandId,
    string LabelResourceKey,
    string IconResourceKey,
    string WorkspaceResourceKey);

/// <summary>
/// Builds a unified command inventory from application actions and every professional ribbon.
/// </summary>
public static class ApplicationCommandCatalog
{
    /// <summary>Returns all user-assignable commands with duplicate command identifiers removed.</summary>
    public static IReadOnlyList<ApplicationCommandDefinition> GetAll()
    {
        var commands = CreateApplicationActions().ToList();
        var knownIds = commands.Select(command => command.CommandId).ToHashSet(StringComparer.Ordinal);

        foreach (RibbonWorkspace workspace in Enum.GetValues<RibbonWorkspace>())
        {
            string workspaceKey = GetWorkspaceResourceKey(workspace);
            foreach (RibbonCommandDefinition command in RibbonCatalog.GetGroups(workspace).SelectMany(group => group.Commands))
            {
                if (knownIds.Add(command.CommandId))
                {
                    commands.Add(new ApplicationCommandDefinition(
                        command.CommandId,
                        command.LabelResourceKey,
                        command.IconResourceKey,
                        workspaceKey));
                }
            }
        }

        return commands;
    }

    /// <summary>Creates commands that live outside the professional ribbon catalog.</summary>
    private static IEnumerable<ApplicationCommandDefinition> CreateApplicationActions()
    {
        yield return new("NewProject", "Action.NewProject", "Icon.New", "Menu.File");
        yield return new("OpenProject", "Action.OpenProject", "Icon.Open", "Menu.File");
        yield return new("Save", "Action.Save", "Icon.Save", "Menu.File");
        yield return new("SaveAs", "Action.SaveAs", "Icon.Save", "Menu.File");
        yield return new("Undo", "Action.Undo", "Icon.Undo", "Menu.Edit");
        yield return new("Redo", "Action.Redo", "Icon.Redo", "Menu.Edit");
        yield return new("Door", "Tool.Door", "Icon.Door", "Ribbon.Architecture");
        yield return new("Window", "Tool.Window", "Icon.Window", "Ribbon.Architecture");
        yield return new("Room", "Tool.Room", "Icon.Room", "Ribbon.Architecture");
        // The only 3D view button: it lives in the quick access toolbar beside Save, not in any ribbon, menu or status bar.
        yield return new("Model3D", "Tool.Model3D", "Icon.Model3DEnhanced", "Menu.View");
        // Beside the 3D button, the only other view button outside the ribbons.
        yield return new("RenderView", "Tool.RenderView", "Icon.Render", "Menu.View");
        yield return new("AiAssistant", "Tool.AiAssistant", "Icon.Assistant", "Menu.View");
        yield return new("ExportIfc", "Action.ExportIfc", "Icon.ExportCube", "Menu.File");
        yield return new("ExportModel3D", "Action.ExportModel3D", "Icon.Model3DEnhanced", "Menu.File");
        yield return new("CustomizeCommands", "CommandCustomization.Title", "Icon.Shortcut", "Ribbon.Manage");
    }

    /// <summary>Maps a workspace to the localized tab name shown in the command editor.</summary>
    private static string GetWorkspaceResourceKey(RibbonWorkspace workspace) => workspace switch
    {
        RibbonWorkspace.Structure => "Ribbon.Structure",
        RibbonWorkspace.Concrete => "Ribbon.Concrete",
        RibbonWorkspace.Steel => "Ribbon.Steel",
        RibbonWorkspace.Systems => "Ribbon.Systems",
        RibbonWorkspace.Insert => "Ribbon.Insert",
        RibbonWorkspace.Annotate => "Ribbon.Annotate",
        RibbonWorkspace.Analyze => "Ribbon.Analyze",
        RibbonWorkspace.MassAndSite => "Ribbon.MassAndSite",
        RibbonWorkspace.Collaborate => "Ribbon.Collaborate",
        RibbonWorkspace.View => "Ribbon.View",
        RibbonWorkspace.Manage => "Ribbon.Manage",
        RibbonWorkspace.AddIns => "Ribbon.AddIns",
        RibbonWorkspace.Modify => "Ribbon.Modify",
        RibbonWorkspace.Wall => "Ribbon.WallTools",
        _ => "App.Name"
    };
}
