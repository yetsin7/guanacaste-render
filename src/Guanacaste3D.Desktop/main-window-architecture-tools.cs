using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Placement of catalog types and the Architecture tab tools that sit on top of the structural tool engine.</summary>
public partial class MainWindow
{
    /// <summary>What a tag click renames.</summary>
    private enum TagTarget { Room, Area }

    private ElementPreset? _structuralPreset;

    /// <summary>The turn given to the next component placed, changed with SPACE.</summary>
    private double _componentRotation;
    private ColumnPreset? _columnPreset;
    private Guid? _placingOpeningTypeId;
    private TagTarget? _tagTarget;

    /// <summary>The way each element kind collects its geometry from the plan.</summary>
    private static StructuralPlacementMode PlacementModeFor(StructuralElementKind kind) => kind switch
    {
        StructuralElementKind.Downspout or StructuralElementKind.IsolatedFoundation or
            StructuralElementKind.Component or StructuralElementKind.Connection or
            StructuralElementKind.ModelText or StructuralElementKind.Planting or StructuralElementKind.ViewReference or StructuralElementKind.SpotElevation or
            StructuralElementKind.SpotCoordinate or StructuralElementKind.Keynote or StructuralElementKind.Symbol or StructuralElementKind.ColorLegend or StructuralElementKind.AirTerminal or
            StructuralElementKind.MechanicalControlDevice or StructuralElementKind.Bolt => StructuralPlacementMode.Point,
        StructuralElementKind.Skylight or StructuralElementKind.Area or StructuralElementKind.Dormer or
            StructuralElementKind.Opening or StructuralElementKind.Shaft or StructuralElementKind.FilledRegion or
            StructuralElementKind.RevisionCloud or StructuralElementKind.Mass or StructuralElementKind.ParkingSpace or StructuralElementKind.Plate => StructuralPlacementMode.Rectangle,
        _ => StructuralPlacementMode.Line
    };

    /// <summary>Arms the placement tool for a catalog section, so every element placed takes its size and style.</summary>
    internal void BeginPresetTool(ElementPreset preset)
    {
        BeginStructuralTool(preset.Kind, PlacementModeFor(preset.Kind));
        _structuralPreset = preset;
        StatusText.Text = FormatText("Status.PresetArmed", preset.Name);
    }

    /// <summary>
    /// Arms planting with the species last used (a broadleaf tree at first). The other species are in the library's
    /// Vegetation category; Space cycles them while the tool is armed.
    /// </summary>
    private void BeginPlantingTool()
    {
        var species = ArchitecturalLibrary.PresetsFor(StructuralElementKind.Planting).ToArray();
        var preset = species.FirstOrDefault(candidate => candidate.Style == _lastPlantingStyle) ?? species[0];
        BeginPresetTool(preset with { Name = LocalizedPresetName(preset) });
    }

    /// <summary>Growth style of the last planting placed or chosen, so the command resumes with it.</summary>
    private ElementStyle _lastPlantingStyle = ElementStyle.Default;

    /// <summary>
    /// While planting or placing a stair, Space steps to the next type of the catalog (species; straight, L, U or spiral
    /// stair), like door swing flipping.
    /// </summary>
    /// <summary>
    /// SPACE turns furniture and fixtures a quarter turn, like Revit's Space while placing a component: the next one placed
    /// while the tool is armed, or the selected one when no tool is armed (one undoable step).
    /// </summary>
    private bool TryRotateComponent()
    {
        if (_structuralTool is { Kind: StructuralElementKind.Component })
        {
            _componentRotation = (_componentRotation + Math.PI / 2) % (2 * Math.PI);
            RenderPlan();
            return true;
        }
        if (_structuralTool is not null || _selectedStructuralId is not Guid id) return false;
        var element = _session.Document.StructuralElements.FirstOrDefault(candidate => candidate.Id == id);
        if (element is not { Kind: StructuralElementKind.Component } || element.Start.DistanceTo(element.End) > 1e-9) return false;
        _session.RotateElement(id, Math.PI / 2);
        StatusText.Text = GetText("Status.ComponentRotated");
        RefreshAll();
        return true;
    }

    private bool TryCyclePlantingSpecies()
    {
        if (_structuralTool is not { Kind: StructuralElementKind.Planting or StructuralElementKind.Stair } tool) return false;
        var types = ArchitecturalLibrary.PresetsFor(tool.Kind).ToArray();
        var current = Array.FindIndex(types, candidate => candidate.Style == (_structuralPreset?.Style ?? ElementStyle.Default));
        var next = types[(current + 1) % types.Length];
        if (tool.Kind == StructuralElementKind.Planting) _lastPlantingStyle = next.Style;
        BeginPresetTool(next with { Name = LocalizedPresetName(next) });
        return true;
    }

    /// <summary>Arms the stair tool with the straight stair; Space chooses an L, U or spiral stair instead.</summary>
    private void BeginStairTool()
    {
        var straight = ArchitecturalLibrary.PresetsFor(StructuralElementKind.Stair).First();
        BeginPresetTool(straight with { Name = LocalizedPresetName(straight) });
        StatusText.Text = GetText("Status.StairTool");
    }

    /// <summary>The display name of a catalog entry in the interface language.</summary>
    private string LocalizedPresetName(ElementPreset preset) =>
        string.IsNullOrWhiteSpace(preset.LocalizationKey) ? preset.Name : GetText(preset.LocalizationKey);

    /// <summary>Arms a tool by element kind with the kind's default section.</summary>
    private void BeginKindTool(StructuralElementKind kind) => BeginStructuralTool(kind, PlacementModeFor(kind));

    /// <summary>Lets the library window arm a tool by kind.</summary>
    internal void BeginKindToolFromLibrary(StructuralElementKind kind) => BeginKindTool(kind);

    /// <summary>Opens the architectural library, the type browser for every element the Architecture tab places.</summary>
    private void OpenArchitecturalLibrary()
    {
        var window = new ArchitecturalLibraryWindow(this, _session) { Owner = this };
        window.Show();
    }

    /// <summary>Starts wall drawing with a library wall assembly selected in the type picker.</summary>
    internal void PlaceLibraryWall(WallType wallType)
    {
        var item = WallTypePicker.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => candidate.Tag is Guid id && id == wallType.Id);
        if (item is not null) WallTypePicker.SelectedItem = item;
        Wall_Click(this, new RoutedEventArgs());
    }

    /// <summary>Starts door or window placement with a specific type.</summary>
    internal void PlaceLibraryOpening(OpeningType type)
    {
        if (type.Kind == OpeningKind.Door) Door_Click(this, new RoutedEventArgs());
        else Window_Click(this, new RoutedEventArgs());
        _placingOpeningTypeId = type.Id;
        StatusText.Text = FormatText("Status.PresetArmed", type.DisplayName);
    }

    /// <summary>Starts column placement with a catalog section.</summary>
    internal void PlaceLibraryColumn(ColumnPreset preset)
    {
        Column_Click(this, new RoutedEventArgs());
        _columnPreset = preset;
        StatusText.Text = FormatText("Status.PresetArmed", preset.Name);
    }

    /// <summary>Creates a floor, ceiling or roof from the walls of the active level with a catalog recipe.</summary>
    internal void PlaceLibrarySlab(SlabPreset preset) => CreateEnclosureSlab(preset.Kind, preset);

    /// <summary>Shows the catalog recipes for a slab role at the pointer, so the user picks a type before it is created.</summary>
    private void ShowSlabPresetMenu(FloorKind kind, Action<SlabPreset>? onPick = null)
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        foreach (var preset in ArchitecturalLibrary.SlabPresets.Where(candidate => candidate.Kind == kind))
        {
            var recipe = preset;
            var item = new MenuItem { Header = preset.Name };
            item.Click += (_, _) => { if (onPick is null) CreateEnclosureSlab(kind, recipe); else onPick(recipe); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>Starts a tag tool: each click on a room or area asks for a new name.</summary>
    private void BeginTagTool(TagTarget target)
    {
        BeginStructuralTool(StructuralElementKind.ModelLine, StructuralPlacementMode.Line);
        CancelStructuralTool();
        _tagTarget = target;
        StatusText.Text = GetText(target == TagTarget.Room ? "Status.TagRoomClick" : "Status.TagAreaClick");
        RenderPlan();
    }

    /// <summary>Renames the room or area under the click and keeps the tag tool armed.</summary>
    private bool TryHandleTagClick(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_tagTarget is not { } target) return false;
        var point = CanvasToWorld(PlanPosition(e));
        Guid? id;
        string current;
        if (target == TagTarget.Room)
        {
            var room = _session.Document.Rooms.LastOrDefault(candidate =>
                candidate.LevelId == _activeLevelId &&
                RoomRegionGeometry.Contains(candidate, point));
            id = room?.Id;
            current = room?.Name ?? string.Empty;
        }
        else
        {
            var area = _session.Document.StructuralElements.LastOrDefault(candidate =>
                candidate.LevelId == _activeLevelId && candidate.Kind == StructuralElementKind.Area &&
                candidate.PlanExtent.Contains(point));
            id = area?.Id;
            current = area?.Name ?? string.Empty;
        }
        if (id is not Guid target_id)
        {
            StatusText.Text = GetText("Status.TagNothingFound");
            return true;
        }
        var name = TextPrompt.Ask(this, GetText(target == TagTarget.Room ? "Prompt.TagRoom.Title" : "Prompt.TagArea.Title"),
            GetText("Prompt.Tag.Label"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), current);
        if (name is null)
        {
            StatusText.Text = GetText("Status.TagCancelled");
            return true;
        }
        try
        {
            if (target == TagTarget.Room) _session.RenameRoom(target_id, name);
            else _session.RenameStructuralElement(target_id, name);
            StatusText.Text = FormatText("Status.TagRenamed", name);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
        return true;
    }

    /// <summary>Adds a skylight on the roof under its rectangle; the session rejects rectangles not fully on a roof.</summary>
    private void CreateSkylightFromTool(Point2 start, Point2 end)
    {
        var id = _session.AddSkylight(_activeLevelId, GetStructuralName(StructuralElementKind.Skylight), start, end);
        _pendingStructuralStart = null;
        _structuralPreviewPoint = null;
        if (id is not Guid created)
        {
            StatusText.Text = GetText("Status.SkylightNoRoof");
            RefreshAll();
            return;
        }
        _selectedStructuralId = created;
        StatusText.Text = FormatText("Status.StructuralCreated", GetStructuralName(StructuralElementKind.Skylight));
        RefreshAll();
    }

    /// <summary>Adds an area rectangle of the area scheme.</summary>
    private void CreateAreaFromTool(Point2 start, Point2 end)
    {
        _pendingStructuralStart = null;
        _structuralPreviewPoint = null;
        try
        {
            _selectedStructuralId = _session.AddArea(_activeLevelId, GetStructuralName(StructuralElementKind.Area), start, end);
            StatusText.Text = FormatText("Status.StructuralCreated", GetStructuralName(StructuralElementKind.Area));
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
    }

    /// <summary>
    /// Opens the property editor for the current selection: slabs and roofs, columns, and structural or
    /// architectural elements. Returns false when the selection has no such properties.
    /// </summary>
    private bool TryEditSelectedElementType(bool gridFocus = false)
    {
        ElementEditorWindow? editor = null;
        if (_selectedStructuralId is Guid structuralId &&
            _session.Document.StructuralElements.Any(element => element.Id == structuralId))
            editor = ElementEditorWindow.ForStructural(_session, structuralId, gridFocus);
        else if (_selectedFloorId is Guid floorId && _session.Document.Floors.Any(floor => floor.Id == floorId))
            editor = ElementEditorWindow.ForFloor(_session, floorId);
        else if (_selectedColumnId is Guid columnId && _session.Document.Columns.Any(column => column.Id == columnId))
            editor = ElementEditorWindow.ForColumn(_session, columnId);
        if (editor is null) return false;
        editor.Owner = this;
        if (editor.ShowDialog() == true) StatusText.Text = GetText("Status.ElementUpdated");
        RefreshAll();
        return true;
    }

    /// <summary>Curtain grid and mullion buttons edit the selected curtain wall, or explain what to select.</summary>
    private void EditCurtainWall(bool gridFocus)
    {
        var selected = _session.Document.StructuralElements
            .FirstOrDefault(element => element.Id == _selectedStructuralId);
        if (selected?.Kind != StructuralElementKind.CurtainWall)
        {
            StatusText.Text = GetText("Status.SelectCurtainWall");
            return;
        }
        TryEditSelectedElementType(gridFocus);
    }

    /// <summary>Resolves the localized label of a structural kind for the new architectural elements.</summary>
    private static string? ArchitecturalNameKey(StructuralElementKind kind) => kind switch
    {
        StructuralElementKind.Ramp => "Tool.Ramp",
        StructuralElementKind.Gutter => "Entity.Gutter",
        StructuralElementKind.Downspout => "Entity.Downspout",
        StructuralElementKind.CurtainWall => "Entity.CurtainWall",
        StructuralElementKind.Skylight => "Entity.Skylight",
        StructuralElementKind.ReferencePlane => "Entity.ReferencePlane",
        StructuralElementKind.RoomSeparator => "Entity.RoomSeparator",
        StructuralElementKind.AreaBoundary => "Entity.AreaBoundary",
        StructuralElementKind.Area => "Entity.Area",
        StructuralElementKind.WallSweep => "Entity.WallSweep",
        StructuralElementKind.WallReveal => "Entity.WallReveal",
        StructuralElementKind.Soffit => "Entity.Soffit",
        StructuralElementKind.Fascia => "Entity.Fascia",
        StructuralElementKind.SlabEdge => "Entity.SlabEdge",
        StructuralElementKind.Planting => "Entity.Planting",
        StructuralElementKind.DetailLine => "Entity.DetailLine",
        StructuralElementKind.FilledRegion => "Entity.FilledRegion",
        StructuralElementKind.RevisionCloud => "Entity.RevisionCloud",
        StructuralElementKind.Insulation => "Entity.Insulation",
        StructuralElementKind.Mass => "Entity.Mass",
        StructuralElementKind.Matchline => "Entity.Matchline",
        StructuralElementKind.ViewReference => "Entity.ViewReference",
        StructuralElementKind.SpotElevation => "Entity.SpotElevation",
        StructuralElementKind.SpotCoordinate => "Entity.SpotCoordinate",
        StructuralElementKind.SpotSlope => "Entity.SpotSlope",
        StructuralElementKind.Keynote => "Entity.Keynote",
        StructuralElementKind.Symbol => "Entity.Symbol",
        StructuralElementKind.ColorLegend => "Entity.ColorLegend",
        StructuralElementKind.Duct => "Entity.Duct",
        StructuralElementKind.DuctPlaceholder => "Entity.DuctPlaceholder",
        StructuralElementKind.FlexDuct => "Entity.FlexDuct",
        StructuralElementKind.CableTray => "Entity.CableTray",
        StructuralElementKind.Conduit => "Entity.Conduit",
        StructuralElementKind.FlexPipe => "Entity.FlexPipe",
        StructuralElementKind.FabricationPart => "Entity.FabricationPart",
        StructuralElementKind.AirTerminal => "Entity.AirTerminal",
        StructuralElementKind.MechanicalControlDevice => "Entity.MechanicalControlDevice",
        StructuralElementKind.DuctStiffener => "Entity.DuctStiffener",
        StructuralElementKind.Plate => "Entity.Plate",
        StructuralElementKind.Bolt => "Entity.Bolt",
        StructuralElementKind.Weld => "Entity.Weld",
        StructuralElementKind.Rebar => "Entity.Rebar",
        StructuralElementKind.FabricSheet => "Entity.FabricSheet",
        StructuralElementKind.ParkingSpace => "Entity.ParkingSpace",
        _ => null
    };
}
