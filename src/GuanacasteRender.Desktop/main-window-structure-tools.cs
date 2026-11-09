using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>How a structural tool collects its geometry from the plan canvas.</summary>
internal enum StructuralPlacementMode
{
    /// <summary>One click places the element at a point.</summary>
    Point,

    /// <summary>Two clicks define the start and end of a line element.</summary>
    Line,

    /// <summary>Two opposite corners define a rectangle that is filled with beams, openings, or shafts.</summary>
    Rectangle,

    /// <summary>Two opposite corners define a rectangle whose elements become one model group.</summary>
    Group
}

/// <summary>Places structural, foundation, connection, and drafting elements from the Estructura ribbon.</summary>
public partial class MainWindow
{
    /// <summary>Center-to-center spacing of the beams created by the beam-system tool, in meters.</summary>
    private const double StructuralBeamSpacing = 1.20;

    /// <summary>Radius in meters within which a connection snaps to an existing member end or column.</summary>
    private const double StructuralJointSnapRadius = 0.60;

    private (StructuralElementKind Kind, StructuralPlacementMode Mode)? _structuralTool;
    private Point2? _pendingStructuralStart;
    private Point2? _structuralPreviewPoint;
    private Guid? _selectedStructuralId;

    /// <summary>Arms a placement tool; leaves any other drawing tool and restores the plan view.</summary>
    private void BeginStructuralTool(StructuralElementKind kind, StructuralPlacementMode mode)
    {
        SetWallTool(false);
        CancelExtendedDrawing();
        _selectedViewId = null;
        ViewTabs.SelectedIndex = 0;
        _structuralTool = (kind, mode);
        _pendingStructuralStart = null;
        _structuralPreviewPoint = null;
        StatusText.Text = mode switch
        {
            StructuralPlacementMode.Group => GetText("Status.ModelGroupFirstCorner"),
            StructuralPlacementMode.Point => FormatText("Status.StructuralPoint", GetStructuralName(kind)),
            StructuralPlacementMode.Rectangle => GetText(kind switch
            {
                StructuralElementKind.Beam => "Status.StructuralFirstCorner",
                StructuralElementKind.Dormer => "Status.DormerFirstCorner",
                StructuralElementKind.Skylight => "Status.SkylightFirstCorner",
                StructuralElementKind.Area => "Status.AreaFirstCorner",
                StructuralElementKind.FilledRegion or StructuralElementKind.RevisionCloud or StructuralElementKind.Mass or StructuralElementKind.ParkingSpace or StructuralElementKind.Plate => "Status.RegionFirstCorner",
                _ => "Status.OpeningFirstCorner"
            }),
            _ => FormatText("Status.StructuralFirstPoint", GetStructuralName(kind))
        };
        RenderPlan();
    }

    /// <summary>Disarms the placement tool and drops its provisional geometry.</summary>
    private void CancelStructuralTool()
    {
        CancelSectionTool();
        CancelGridTool();
        _structuralTool = null;
        _structuralPreset = null;
        _placingTopoPoints = false;
        CancelElectricalTools();
        CancelPlumbingTools();
        _tagTarget = null;
        CancelArchitectureTools();
        _pendingStructuralStart = null;
        _structuralPreviewPoint = null;
    }

    /// <summary>Tracks the pointer while a structural tool is armed so its preview follows the cursor.</summary>
    private bool TryUpdateStructuralPreview(MouseEventArgs e)
    {
        if (TryUpdateGridPreview(e) || TryUpdateSectionPreview(e) || TryUpdateSketchPreview(e)) return true;
        if (_structuralTool is not { } tool) return false;
        var pointer = SnapWallPoint(CanvasToWorld(PlanPosition(e)));
        _structuralPreviewPoint = ResolveStructuralPoint(tool.Kind, pointer);
        RenderPlan();
        return true;
    }

    /// <summary>Places a point element at one click, or collects the two ends of a line or rectangle.</summary>
    private bool TryHandleStructuralPlacement(MouseButtonEventArgs e)
    {
        if (TryHandleTagClick(e) || TryHandleSlabSketchClick(e) || TryHandleWallPick(e) || TryHandleGroupPlace(e) ||
            TryHandleWorkPlanePick(e)) return true;
        if (_structuralTool is not { } tool) return false;
        var point = ResolveStructuralPoint(tool.Kind,
            SnapWallPoint(CanvasToWorld(PlanPosition(e))));
        if (tool.Mode == StructuralPlacementMode.Point)
        {
            if (tool.Kind == StructuralElementKind.ModelText) PlaceModelText(point);
            else if (tool.Kind == StructuralElementKind.ViewReference) PlaceViewReference(point);
            else if (tool.Kind == StructuralElementKind.Keynote) PlaceKeynote(point);
            else if (tool.Kind == StructuralElementKind.Symbol) PlaceSymbol(point);
            else if (tool.Kind == StructuralElementKind.Bolt && _pendingBolts is not null) PlaceBolts(point);
            else if (tool.Kind == StructuralElementKind.Opening) CreateOpeningFromTool(tool.Kind, point, point);
            else CreateStructuralFromTool(tool.Kind, point, point);
            return true;
        }

        if (_pendingStructuralStart is not Point2 start)
        {
            _pendingStructuralStart = point;
            _structuralPreviewPoint = point;
            StatusText.Text = tool.Mode switch
            {
                StructuralPlacementMode.Group => GetText("Status.ModelGroupSecondCorner"),
                StructuralPlacementMode.Rectangle when tool.Kind == StructuralElementKind.Beam =>
                    GetText("Status.StructuralSecondCorner"),
                StructuralPlacementMode.Rectangle when tool.Kind == StructuralElementKind.Dormer =>
                    GetText("Status.DormerSecondCorner"),
                StructuralPlacementMode.Rectangle when tool.Kind == StructuralElementKind.Skylight =>
                    GetText("Status.SkylightSecondCorner"),
                StructuralPlacementMode.Rectangle when tool.Kind == StructuralElementKind.Area =>
                    GetText("Status.AreaSecondCorner"),
                StructuralPlacementMode.Rectangle when tool.Kind is StructuralElementKind.FilledRegion or StructuralElementKind.RevisionCloud or StructuralElementKind.Mass or StructuralElementKind.ParkingSpace or StructuralElementKind.Plate =>
                    GetText("Status.RegionSecondCorner"),
                StructuralPlacementMode.Rectangle => GetText("Status.OpeningSecondCorner"),
                _ => FormatText("Status.StructuralSecondPoint", GetStructuralName(tool.Kind))
            };
            RenderPlan();
            return true;
        }

        if (tool.Mode == StructuralPlacementMode.Group)
        {
            _pendingStructuralStart = null;
            _structuralPreviewPoint = null;
            var members = _session.AddStructuralGroup(_activeLevelId, start, point);
            StatusText.Text = members == 0
                ? GetText("Status.ModelGroupNeedsTwo")
                : FormatText("Status.ModelGroupCreated", members);
            RefreshAll();
            return true;
        }

        if (tool.Kind == StructuralElementKind.Beam && tool.Mode == StructuralPlacementMode.Rectangle)
        {
            var count = _session.AddBeamSystem(_activeLevelId, GetStructuralName(tool.Kind),
                start, point, StructuralBeamSpacing);
            StatusText.Text = FormatText("Status.BeamSystemCreated", count);
        }
        else if (tool.Kind is StructuralElementKind.Opening or StructuralElementKind.Shaft)
        {
            CreateOpeningFromTool(tool.Kind, start, point);
            return true;
        }
        else if (tool.Kind == StructuralElementKind.Dormer)
        {
            CreateDormerFromTool(start, point);
            return true;
        }
        else if (tool.Kind == StructuralElementKind.Skylight)
        {
            CreateSkylightFromTool(start, point);
            return true;
        }
        else if (tool.Kind == StructuralElementKind.Area)
        {
            CreateAreaFromTool(start, point);
            return true;
        }
        else if (tool.Kind == StructuralElementKind.SpotSlope)
        {
            CreateSpotSlope(start, point);
            return true;
        }
        else
        {
            CreateStructuralFromTool(tool.Kind, start, point);
            return true;
        }
        _pendingStructuralStart = null;
        _structuralPreviewPoint = null;
        RefreshAll();
        return true;
    }

    /// <summary>
    /// Adds an opening or shaft through the session, which rejects rectangles that do not fit inside a floor.
    /// The pending corner is cleared either way so the user can try again from the next click.
    /// </summary>
    private void CreateOpeningFromTool(StructuralElementKind kind, Point2 start, Point2 end)
    {
        var id = _session.AddOpening(_activeLevelId, kind, GetStructuralName(kind), start, end);
        _pendingStructuralStart = null;
        _structuralPreviewPoint = null;
        if (id is not Guid created)
        {
            StatusText.Text = GetText(kind == StructuralElementKind.Shaft ? "Status.ShaftNoFloor" : "Status.OpeningNoFloor");
            RefreshAll();
            return;
        }
        _selectedStructuralId = created;
        StatusText.Text = FormatText("Status.StructuralCreated", GetStructuralName(kind));
        RefreshAll();
    }

    /// <summary>
    /// Adds a dormer on the roof under its rectangle. The session rejects rectangles that are not fully
    /// inside a roof of this level, and the pending corner is cleared either way.
    /// </summary>
    private void CreateDormerFromTool(Point2 start, Point2 end)
    {
        var id = _session.AddDormer(_activeLevelId, GetStructuralName(StructuralElementKind.Dormer), start, end);
        _pendingStructuralStart = null;
        _structuralPreviewPoint = null;
        if (id is not Guid created)
        {
            StatusText.Text = GetText("Status.DormerNoRoof");
            RefreshAll();
            return;
        }
        _selectedStructuralId = created;
        StatusText.Text = FormatText("Status.StructuralCreated", GetStructuralName(StructuralElementKind.Dormer));
        RefreshAll();
    }

    /// <summary>Adds one element from the active tool, selects it, and keeps the tool armed for the next one.</summary>
    private void CreateStructuralFromTool(StructuralElementKind kind, Point2 start, Point2 end)
    {
        try
        {
            if (kind == StructuralElementKind.Stair)
            {
                // As in Revit, a stair rises to the level above (when there is one) and gets its railings at once.
                var preset = _structuralPreset is { Kind: StructuralElementKind.Stair } chosen
                    ? chosen
                    : ArchitecturalLibrary.PresetsFor(StructuralElementKind.Stair).First();
                using var group = _session.BeginGroup("Transaction.AddStair");
                _selectedStructuralId = _session.AddElementFromPreset(_activeLevelId,
                    preset with { Height = StairPlacementHeight(preset.Height, preset.Offset) }, start, end);
                _session.AddRailingsOnHost(_selectedStructuralId.Value);
                group.Assimilate();
            }
            else
            {
                _selectedStructuralId = _structuralPreset is { } preset && preset.Kind == kind
                    ? _session.AddElementFromPreset(_activeLevelId, preset, start, end,
                        kind == StructuralElementKind.Component ? _componentRotation : 0)
                    : _session.AddStructuralElement(_activeLevelId, kind, GetStructuralName(kind), start, end);
            }
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
            return;
        }
        _pendingStructuralStart = null;
        _structuralPreviewPoint = null;
        StatusText.Text = FormatText("Status.StructuralCreated", GetStructuralName(kind));
        RefreshAll();
    }

    /// <summary>Creates connections at every unconnected joint on the active level.</summary>
    private void CreateConnectionsAtJoints()
    {
        CancelStructuralTool();
        var count = _session.AddConnectionsAtJoints(_activeLevelId, GetStructuralName(StructuralElementKind.Connection));
        StatusText.Text = count == 0
            ? GetText("Status.ConnectionsNone")
            : FormatText("Status.ConnectionsCreated", count);
        RefreshAll();
    }

    /// <summary>Snaps a connection to the nearest member end or column center, otherwise keeps the point.</summary>
    private Point2 ResolveStructuralPoint(StructuralElementKind kind, Point2 point)
    {
        if (kind != StructuralElementKind.Connection) return point;
        var candidates = _session.Document.StructuralElements
            .Where(element => element.LevelId == _activeLevelId && element.Length > 0)
            .SelectMany(element => new[] { element.Start, element.End })
            .Concat(_session.Document.Columns
                .Where(column => column.LevelId == _activeLevelId)
                .Select(column => column.Position))
            .Where(candidate => candidate.DistanceTo(point) <= StructuralJointSnapRadius)
            .ToArray();
        return candidates.Length == 0 ? point : candidates.MinBy(candidate => candidate.DistanceTo(point));
    }

    /// <summary>Resolves the localized display name used for a structural element kind.</summary>
    private string GetStructuralName(StructuralElementKind kind) => GetText(ArchitecturalNameKey(kind) ?? kind switch
    {
        StructuralElementKind.Stair => "Tool.Stair",
        StructuralElementKind.Railing => "Tool.Railing",
        StructuralElementKind.Beam => "Entity.Beam",
        StructuralElementKind.Brace => "Entity.Brace",
        StructuralElementKind.Truss => "Entity.Truss",
        StructuralElementKind.WallFoundation => "Entity.WallFoundation",
        StructuralElementKind.IsolatedFoundation => "Entity.IsolatedFoundation",
        StructuralElementKind.Component => "Entity.Component",
        StructuralElementKind.Connection => "Entity.Connection",
        StructuralElementKind.Opening => "Entity.Opening",
        StructuralElementKind.Shaft => "Entity.Shaft",
        StructuralElementKind.ModelText => "Entity.ModelText",
        StructuralElementKind.Dormer => "Entity.Dormer",
        _ => "Entity.ModelLine"
    });

}
