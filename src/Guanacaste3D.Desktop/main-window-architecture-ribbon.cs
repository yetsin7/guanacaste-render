using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Gets the wall type selector hosted by the architecture ribbon.
    /// </summary>
    private ComboBox WallTypePicker => ArchitectureRibbonControl.WallTypePickerControl;

    /// <summary>
    /// Gets the ribbon button whose emphasis reflects wall drawing mode.
    /// </summary>
    private Button WallCommandButton => ArchitectureRibbonControl.WallCommandButtonControl;

    /// <summary>
    /// Routes architecture ribbon commands to implemented workflows and reports
    /// the honest availability state of planned tools.
    /// </summary>
    private void ArchitectureRibbon_CommandInvoked(
        object? sender,
        ArchitectureCommandEventArgs e)
    {
        var eventArguments = new RoutedEventArgs();
        if (ExecuteArchitectureCommand(e.Command)) return;
        switch (e.Command)
        {
            case ArchitectureCommand.Modify:
                Select_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Move:
                Move_Click(this, eventArguments);
                break;
            case ArchitectureCommand.CreateSimilar:
                CreateSimilar_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Wall:
                Wall_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Door:
                Door_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Window:
                Window_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Floor:
                Floor_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Ceiling:
                Ceiling_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Roof:
                Roof_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Column:
                Column_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Stair:
                BeginStairTool();
                break;
            case ArchitectureCommand.Railing:
                BeginStructuralTool(StructuralElementKind.Railing, StructuralPlacementMode.Line);
                break;
            case ArchitectureCommand.Component:
                OpenArchitecturalLibrary();
                break;
            case ArchitectureCommand.Ramp:
                BeginKindTool(StructuralElementKind.Ramp);
                break;
            case ArchitectureCommand.CurtainSystem:
                BeginKindTool(StructuralElementKind.CurtainWall);
                break;
            case ArchitectureCommand.CurtainGrid:
                EditCurtainWall(gridFocus: true);
                break;
            case ArchitectureCommand.Mullion:
                EditCurtainWall(gridFocus: false);
                break;
            case ArchitectureCommand.ModelText:
                BeginKindTool(StructuralElementKind.ModelText);
                break;
            case ArchitectureCommand.ModelLine:
                BeginKindTool(StructuralElementKind.ModelLine);
                break;
            case ArchitectureCommand.ModelGroup:
                BeginStructuralTool(StructuralElementKind.ModelLine, StructuralPlacementMode.Group);
                break;
            case ArchitectureCommand.RoomSeparator:
                BeginKindTool(StructuralElementKind.RoomSeparator);
                break;
            case ArchitectureCommand.RoomSeparatorCurve:
                BeginCurvedRoomSeparatorTool();
                break;
            case ArchitectureCommand.TagRoom:
                BeginTagTool(TagTarget.Room);
                break;
            case ArchitectureCommand.Area:
                BeginKindTool(StructuralElementKind.Area);
                break;
            case ArchitectureCommand.AreaBoundary:
                BeginKindTool(StructuralElementKind.AreaBoundary);
                break;
            case ArchitectureCommand.TagArea:
                BeginTagTool(TagTarget.Area);
                break;
            case ArchitectureCommand.FaceOpening:
                BeginStructuralTool(StructuralElementKind.Opening, StructuralPlacementMode.Point);
                break;
            case ArchitectureCommand.ShaftOpening:
                BeginStructuralTool(StructuralElementKind.Shaft, StructuralPlacementMode.Rectangle);
                break;
            case ArchitectureCommand.VerticalOpening:
                BeginStructuralTool(StructuralElementKind.Opening, StructuralPlacementMode.Rectangle);
                break;
            case ArchitectureCommand.DormerOpening:
                BeginStructuralTool(StructuralElementKind.Dormer, StructuralPlacementMode.Rectangle);
                break;
            case ArchitectureCommand.WallOpening:
                Window_Click(this, eventArguments);
                break;
            case ArchitectureCommand.SetWorkPlane:
                Levels_Click(this, eventArguments);
                break;
            case ArchitectureCommand.ShowWorkPlane:
                ToggleGrid_Click(this, eventArguments);
                break;
            case ArchitectureCommand.ReferencePlane:
                BeginKindTool(StructuralElementKind.ReferencePlane);
                break;
            case ArchitectureCommand.Room:
                Room_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Level:
                AddLevel_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Levels:
                Levels_Click(this, eventArguments);
                break;
            case ArchitectureCommand.OpeningTypes:
                OpeningTypes_Click(this, eventArguments);
                break;
            case ArchitectureCommand.ApplyWallType:
                ApplyWallType_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Grid:
                ToggleGrid_Click(this, eventArguments);
                break;
            case ArchitectureCommand.Viewer:
                ShowModel_Click(this, eventArguments);
                break;
            default:
                ShowPlannedArchitectureCommand(e.Command);
                break;
        }
    }

    /// <summary>
    /// Opens the available type and parameter editor from the properties palette.
    /// </summary>
    private void ArchitecturePropertiesEditType_Click(object sender, RoutedEventArgs e)
    {
        if (ViewTabs.SelectedIndex == 3) { EditSheet_Click(sender, e); return; }
        if (_selectedWallId is null && TryEditSelectedElementType()) return;
        var selectedTypeId = _selectedWallId is Guid wallId
            ? _session.Document.Walls.Single(wall => wall.Id == wallId).WallTypeId
            : (WallTypePicker.SelectedItem as ComboBoxItem)?.Tag as Guid? ?? BuiltInWallTypes.Basic200Id;
        var editor = new WallTypeEditorWindow(_session, selectedTypeId) { Owner = this };
        if (editor.ShowDialog() == true)
            StatusText.Text = GetText("Status.WallTypeUpdated");
    }

    /// <summary>
    /// Reports commands that are intentionally visible for workspace completeness
    /// but do not yet have a BIM authoring implementation.
    /// </summary>
    private void ShowPlannedArchitectureCommand(ArchitectureCommand command)
    {
        StatusText.Text = string.Format(
            CultureInfo.CurrentCulture,
            GetText("Status.ArchitectureCommandPlanned"),
            GetArchitectureCommandLabel(command));
    }

    /// <summary>
    /// Resolves the localized label used when describing a planned command.
    /// </summary>
    private string GetArchitectureCommandLabel(ArchitectureCommand command)
    {
        var resourceKey = command switch
        {
            ArchitectureCommand.Component => "Tool.Component",
            ArchitectureCommand.Column => "Tool.Column",
            ArchitectureCommand.Roof => "Tool.Roof",
            ArchitectureCommand.Ceiling => "Tool.Ceiling",
            ArchitectureCommand.CurtainSystem => "Tool.CurtainSystem",
            ArchitectureCommand.CurtainGrid => "Tool.CurtainGrid",
            ArchitectureCommand.Mullion => "Tool.Mullion",
            ArchitectureCommand.Railing => "Tool.Railing",
            ArchitectureCommand.Ramp => "Tool.Ramp",
            ArchitectureCommand.Stair => "Tool.Stair",
            ArchitectureCommand.ModelText => "Tool.ModelText",
            ArchitectureCommand.ModelLine => "Tool.ModelLine",
            ArchitectureCommand.ModelGroup => "Tool.ModelGroup",
            ArchitectureCommand.RoomSeparator => "Tool.RoomSeparator",
            ArchitectureCommand.TagRoom => "Tool.TagRoom",
            ArchitectureCommand.Area => "Tool.Area",
            ArchitectureCommand.AreaBoundary => "Tool.AreaBoundary",
            ArchitectureCommand.TagArea => "Tool.TagArea",
            ArchitectureCommand.FaceOpening => "Tool.FaceOpening",
            ArchitectureCommand.ShaftOpening => "Tool.ShaftOpening",
            ArchitectureCommand.WallOpening => "Tool.WallOpening",
            ArchitectureCommand.VerticalOpening => "Tool.VerticalOpening",
            ArchitectureCommand.DormerOpening => "Tool.DormerOpening",
            ArchitectureCommand.SetWorkPlane => "Tool.SetWorkPlane",
            ArchitectureCommand.ShowWorkPlane => "Tool.ShowWorkPlane",
            ArchitectureCommand.ReferencePlane => "Tool.ReferencePlane",
            _ => "Ribbon.Architecture",
        };

        return GetText(resourceKey);
    }
}
