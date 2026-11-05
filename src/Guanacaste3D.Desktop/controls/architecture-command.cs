namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Identifies a command exposed by the architecture authoring ribbon.
/// </summary>
public enum ArchitectureCommand
{
    Modify,
    Move,
    CreateSimilar,
    Wall,
    Door,
    Window,
    Component,
    Column,
    Roof,
    Ceiling,
    Floor,
    CurtainSystem,
    CurtainGrid,
    Mullion,
    Railing,
    Ramp,
    Stair,
    ModelText,
    ModelLine,
    ModelGroup,
    Room,
    RoomSeparator,
    RoomSeparatorCurve,
    TagRoom,
    Area,
    AreaBoundary,
    TagArea,
    FaceOpening,
    ShaftOpening,
    WallOpening,
    VerticalOpening,
    DormerOpening,
    Level,
    Levels,
    Grid,
    SetWorkPlane,
    ShowWorkPlane,
    ReferencePlane,
    Viewer,
    OpeningTypes,
    ApplyWallType,
    WallArchitectural,
    WallStructural,
    WallByFace,
    WallSweep,
    WallReveal,
    ComponentPlace,
    ComponentInPlace,
    ColumnArchitectural,
    ColumnStructural,
    RoofByFootprint,
    RoofByExtrusion,
    RoofByFace,
    RoofSoffit,
    RoofFascia,
    RoofGutter,
    CeilingAutomatic,
    CeilingSketch,
    FloorArchitectural,
    FloorStructural,
    FloorByFace,
    SlabEdge,
    RailingSketch,
    RailingOnHost,
    GroupCreate,
    GroupPlace,
    RoomAutomatic,
    TagAllRooms,
    TagAllAreas,
    AreaByBoundary,
    Deselect,
    WorkPlaneByLevel,
    WorkPlanePickReference,
}

/// <summary>
/// Carries the architecture command selected by the user.
/// </summary>
public sealed class ArchitectureCommandEventArgs : EventArgs
{
    /// <summary>
    /// Initializes an event payload for the selected architecture command.
    /// </summary>
    /// <param name="command">The command requested from the ribbon.</param>
    public ArchitectureCommandEventArgs(ArchitectureCommand command)
    {
        Command = command;
    }

    /// <summary>
    /// Gets the requested architecture command.
    /// </summary>
    public ArchitectureCommand Command { get; }
}
