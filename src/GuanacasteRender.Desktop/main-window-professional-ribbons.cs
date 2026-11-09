using System.Globalization;
using System.Windows;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Routes shared ribbon commands to implemented workflows while keeping planned
    /// tools visible with an explicit availability message.
    /// </summary>
    private void ProfessionalRibbon_CommandInvoked(object? sender, ProfessionalRibbonCommandEventArgs e)
    {
        if (!ExecuteApplicationCommand(e.CommandId))
        {
            ShowPlannedRibbonCommand(e);
        }
    }

    /// <summary>
    /// Executes a command shared by ribbons, quick access buttons, and keyboard sequences.
    /// </summary>
    private bool ExecuteApplicationCommand(string commandId)
    {
        RecordAutomationStep(commandId);
        var eventArguments = new RoutedEventArgs();
        CancelStructuralTool();
        switch (commandId)
        {
            case "NewProject": New_Click(this, eventArguments); return true;
            case "OpenProject": Open_Click(this, eventArguments); return true;
            case "Save": Save_Click(this, eventArguments); return true;
            case "SaveAs": SaveAs_Click(this, eventArguments); return true;
            case "Undo": Undo_Click(this, eventArguments); return true;
            case "Redo": Redo_Click(this, eventArguments); return true;
            case "Modify": Select_Click(this, eventArguments); return true;
            case "Wall": Wall_Click(this, eventArguments); return true;
            case "Door": Door_Click(this, eventArguments); return true;
            case "Window": Window_Click(this, eventArguments); return true;
            case "Room": Room_Click(this, eventArguments); return true;
            case "Floor": Floor_Click(this, eventArguments); return true;
            case "Level": AddLevel_Click(this, eventArguments); return true;
            case "Grid": BeginGridTool(); return true;
            case "Text": BeginKindTool(StructuralElementKind.ModelText); return true;
            case "Viewer":
            case "Model3D": ShowModel_Click(this, eventArguments); return true;
            case "RenderView": ShowRender_Click(this, eventArguments); return true;
            case "Move": Move_Click(this, eventArguments); return true;
            case "CopyElement": Duplicate_Click(this, eventArguments); return true;
            case "CreateSimilar": CreateSimilar_Click(this, eventArguments); return true;
            case "Rotate": Rotate_Click(this, eventArguments); return true;
            case "Delete": Delete_Click(this, eventArguments); return true;
            case "Plan": ShowPlan_Click(this, eventArguments); return true;
            case "Elevation": ShowElevation_Click(this, eventArguments); return true;
            case "Section": BeginSectionTool(); return true;
            case "PropertyLine": DefineLot(); return true;
            case "Schedule": ShowSchedule_Click(this, eventArguments); return true;
            case "Sheet": AddSheet_Click(this, eventArguments); return true;
            case "Render": StartRender_Click(this, eventArguments); return true;
            case "Fit": FitPlan_Click(this, eventArguments); return true;
            case "ProjectParameters":
            case "WallProperties":
            case "WallTypes": ProjectParameters_Click(this, eventArguments); return true;
            case "Levels": Levels_Click(this, eventArguments); return true;
            case "Schedules": Schedules_Click(this, eventArguments); return true;
            case "OpeningTypes":
            case "WallOpenings": OpeningTypes_Click(this, eventArguments); return true;
            case "ImportCad":
            case "LinkCad":
            case "LinkIfc":
            case "LinkNativeProject":
            case "CoordinationModel":
            case "InsertFromFile": ImportModel_Click(this, eventArguments); return true;
            case "ManageLinks": LinkedModels_Click(this, eventArguments); return true;
            case "AiAssistant": OpenAiAssistant(); return true;
            case "TileViews": EnterTileViews(); return true;
            case "TabViews": ExitTileViews(); return true;
            case "Callout": CreateCalloutFromSelection(); return true;
            case "ViewTemplates": ShowViewTemplatesMenu(); return true;
            case "TagAll":
            case "TagByCategory": ToggleElementTags(); return true;
            case "AlignedDimension": AutoDimensionActiveLevel(); return true;
            case "ExportIfc": ExportIfc_Click(this, eventArguments); return true;
            case "ExportModel3D": ExportModel3D_Click(this, eventArguments); return true;
            case "CustomizeCommands": CustomizeCommands_Click(this, eventArguments); return true;
            case "CloseHiddenWindows": CloseInactiveViews(); return true;
            case "About": About_Click(this, eventArguments); return true;
            default: return TryExecutePresentationCommand(commandId) || TryExecuteWallJoinCommand(commandId) ||
                            TryExecuteWallEditCommand(commandId) || TryExecuteViewCategoryCommand(commandId) || TryExecuteManageCommand(commandId) || TryExecuteView3DCommand(commandId) || TryExecuteBulkCommand(commandId) ||
                            TryExecuteStagesCommand(commandId) || TryExecuteWallProfileCommand(commandId) || TryExecuteUnitsCommand(commandId) || TryExecuteGroupCommand(commandId) || TryExecuteSiteCommand(commandId) || TryExecuteMassCommand(commandId) || TryExecuteSheetAidCommand(commandId) || TryExecuteMarkCommand(commandId) || TryExecuteSiteImportCommand(commandId) || TryExecuteCollaborationCommand(commandId) || TryExecuteCoordinationCommand(commandId) || TryExecuteTableImportCommand(commandId) || TryExecuteMepCommand(commandId) || TryExecuteSteelCommand(commandId) || TryExecuteConcreteCommand(commandId) || TryExecuteAnalysisCommand(commandId) || TryExecuteGeometryToolCommand(commandId) || TryExecuteAddInsCommand(commandId) || TryExecuteStudyCommand(commandId) || TryExecuteFamilyLibraryCommand(commandId) || TryExecuteAutomationCommand(commandId) || TryExecuteStructuralCommand(commandId);
        }
    }

    /// <summary>
    /// Reports that a visible workspace command is not yet backed by a BIM operation.
    /// </summary>
    private void ShowPlannedRibbonCommand(ProfessionalRibbonCommandEventArgs command)
    {
        StatusText.Text = string.Format(
            CultureInfo.CurrentCulture,
            GetText("Status.RibbonCommandPlanned"),
            GetWorkspaceLabel(command.Workspace),
            command.DisplayName);
    }

    /// <summary>
    /// Resolves the localized tab label used in planned-command status feedback.
    /// </summary>
    private string GetWorkspaceLabel(RibbonWorkspace workspace) => GetText(workspace switch
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
    });
}
