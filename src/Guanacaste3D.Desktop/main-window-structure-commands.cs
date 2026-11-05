using System.Windows;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Maps a Structure ribbon command to its tool. Commands that reuse an existing workflow are delegated
    /// to it; commands whose BIM operation is not modeled yet return false and keep the planned message.
    /// </summary>
    private bool TryExecuteStructuralCommand(string commandId)
    {
        var eventArguments = new RoutedEventArgs();
        // The catalog sends its own ids ("Beam"); the table below is keyed by label key ("RibbonCommand.Beam").
        commandId = commandId switch
        {
            "StructColumn" => "RibbonCommand.Column",
            "WallByFace" => "RibbonCommand.WallFoundation",
            "ConnectionAuto" => "RibbonCommand.ConnectionAutomation",
            _ when !commandId.StartsWith("RibbonCommand.", StringComparison.Ordinal) => "RibbonCommand." + commandId,
            _ => commandId
        };
        switch (commandId)
        {
            case "RibbonCommand.LoadFamily":
                ImportFamily_Click(this, eventArguments); return true;
            case "RibbonCommand.Beam":
                BeginStructuralTool(StructuralElementKind.Beam, StructuralPlacementMode.Line); return true;
            case "RibbonCommand.Truss":
                BeginStructuralTool(StructuralElementKind.Truss, StructuralPlacementMode.Line); return true;
            case "RibbonCommand.Brace":
                BeginStructuralTool(StructuralElementKind.Brace, StructuralPlacementMode.Line); return true;
            case "RibbonCommand.BeamSystem":
                BeginStructuralTool(StructuralElementKind.Beam, StructuralPlacementMode.Rectangle); return true;
            case "RibbonCommand.WallFoundation":
                BeginStructuralTool(StructuralElementKind.WallFoundation, StructuralPlacementMode.Line); return true;
            case "RibbonCommand.IsolatedFoundation":
                BeginStructuralTool(StructuralElementKind.IsolatedFoundation, StructuralPlacementMode.Point); return true;
            case "RibbonCommand.Component":
                BeginStructuralTool(StructuralElementKind.Component, StructuralPlacementMode.Point); return true;
            case "RibbonCommand.Connection":
                BeginStructuralTool(StructuralElementKind.Connection, StructuralPlacementMode.Point); return true;
            case "RibbonCommand.ConnectionAutomation":
                CreateConnectionsAtJoints(); return true;
            case "RibbonCommand.ModelLine":
                BeginStructuralTool(StructuralElementKind.ModelLine, StructuralPlacementMode.Line); return true;
            case "RibbonCommand.OpeningByFace":
                BeginStructuralTool(StructuralElementKind.Opening, StructuralPlacementMode.Point); return true;
            case "RibbonCommand.VerticalOpening":
                BeginStructuralTool(StructuralElementKind.Opening, StructuralPlacementMode.Rectangle); return true;
            case "RibbonCommand.ShaftOpening":
                BeginStructuralTool(StructuralElementKind.Shaft, StructuralPlacementMode.Rectangle); return true;
            case "RibbonCommand.ModelText":
                BeginStructuralTool(StructuralElementKind.ModelText, StructuralPlacementMode.Point); return true;
            case "RibbonCommand.DormerOpening":
                BeginStructuralTool(StructuralElementKind.Dormer, StructuralPlacementMode.Rectangle); return true;
            case "RibbonCommand.ReferencePlane":
                BeginKindTool(StructuralElementKind.ReferencePlane); return true;
            case "RibbonCommand.ArchitecturalLibrary":
                OpenArchitecturalLibrary(); return true;
            case "RibbonCommand.LightingFixture":
                BeginElectricalTool(Luminaires); return true;
            case "RibbonCommand.ElectricalDevice":
                BeginElectricalTool(Receptacles); return true;
            case "RibbonCommand.LightSwitch":
                BeginElectricalTool(Switches); return true;
            case "RibbonCommand.ElectricalEquipment":
                BeginElectricalTool([ElectricalDeviceKind.Panel]); return true;
            case "RibbonCommand.Wire":
                BeginWiringTool(); return true;
            case "RibbonCommand.PanelSchedule":
                OpenPanelSchedule(); return true;
            case "RibbonCommand.PhotovoltaicSystem":
                BeginElectricalTool(Solar); return true;
            case "RibbonCommand.MechanicalEquipment":
                BeginElectricalTool(Cooling); return true;
            case "RibbonCommand.PlumbingService":
                BeginPlumbingPointTool(); return true;
            case "RibbonCommand.Pipe":
                BeginPipingTool(); return true;
            case "RibbonCommand.ConnectPlumbing":
                ConnectAllPlumbing(); return true;
            case "RibbonCommand.PlumbingSchedule":
                OpenPlumbingSchedule(); return true;
            case "RibbonCommand.Toposolid":
                BeginToposolidSketch(false); return true;
            case "RibbonCommand.GradedRegion":
                BeginTopoPointTool(); return true;
            case "RibbonCommand.LabelContours":
                _labelContours = !_labelContours;
                StatusText.Text = GetText(_labelContours ? "Status.ContourLabelsOn" : "Status.ContourLabelsOff");
                RenderPlan();
                return true;
            case "RibbonCommand.MassPlacement":
                BeginMassPlacement(); return true;
            case "RibbonCommand.SiteComponent":
            case "RibbonCommand.Planting":
                BeginPlantingTool(); return true;
            case "RibbonCommand.ModelGroup":
                BeginStructuralTool(StructuralElementKind.ModelLine, StructuralPlacementMode.Group); return true;
            case "RibbonCommand.Column":
                Column_Click(this, eventArguments); return true;
            case "RibbonCommand.FoundationSlab":
                Floor_Click(this, eventArguments); return true;
            case "RibbonCommand.WallOpening":
                Window_Click(this, eventArguments); return true;
            case "RibbonCommand.Level":
                AddLevel_Click(this, eventArguments); return true;
            case "RibbonCommand.Grid":
                BeginGridTool(); return true;
            case "RibbonCommand.ShowWorkPlane":
                ToggleGrid_Click(this, eventArguments); return true;
            case "RibbonCommand.SetWorkPlane":
                Levels_Click(this, eventArguments); return true;
            case "RibbonCommand.Viewer":
                ShowModel_Click(this, eventArguments); return true;
            default:
                return false;
        }
    }

}
