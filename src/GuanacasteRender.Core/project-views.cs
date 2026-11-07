namespace Guanacaste3D.Core;

/// <summary>Classifies coordinated model and documentation views.</summary>
public enum ProjectViewKind
{
    Plan,
    Model3D,
    Elevation,
    Section,
    Schedule
}

/// <summary>
/// The direction an elevation or section looks toward. A north elevation shows the north face of the building, seen
/// from the south; its symbol in the plan sits south of the building and points north.
/// </summary>
public enum ViewOrientation
{
    None,
    North,
    East,
    South,
    West
}

/// <summary>Controls whether a section coordinates the complete model or isolates its load-bearing construction.</summary>
public enum SectionDisplayMode
{
    Coordinated,
    Structural
}

/// <summary>
/// Defines the vertical slice shown by a plan, measured from its associated level. The cut plane controls which hosted
/// openings are drawn; the lower and upper limits keep the model context intentionally small and predictable.
/// </summary>
public sealed record PlanViewRange(double BottomOffset, double CutOffset, double TopOffset)
{
    /// <summary>The architectural default shows doors and windows without including the storey above.</summary>
    public static PlanViewRange Architectural { get; } = new(0, 1.2, 2.3);

    /// <summary>Returns whether all offsets are finite, ordered, and within the supported modelling range.</summary>
    public bool IsValid => double.IsFinite(BottomOffset + CutOffset + TopOffset) &&
        BottomOffset is >= -10 and <= 1000 && BottomOffset <= CutOffset && CutOffset <= TopOffset &&
        TopOffset - BottomOffset is >= 0.1 and <= 1000;

    /// <summary>Returns whether a vertical interval relative to the level intersects this view range.</summary>
    public bool Intersects(double bottom, double top) => top >= BottomOffset && bottom <= TopOffset;
}

/// <summary>
/// Stores the plan marker and horizontal field covered by an elevation. A null marker lets legacy views follow the
/// building automatically until the user drags them for the first time.
/// </summary>
public sealed record ElevationMarker(Point2 Position, double Width, double Depth)
{
    /// <summary>Returns whether the marker and its field dimensions can be used safely.</summary>
    public bool IsValid => double.IsFinite(Position.X + Position.Y + Width + Depth) &&
        Width is >= 0.5 and <= 100000 && Depth is >= 0.5 and <= 100000;
}

/// <summary>Represents a persistent view generated from the shared building document.</summary>
public sealed record ProjectView(
    Guid Id,
    string Name,
    ProjectViewKind Kind,
    Guid? LevelId,
    ViewOrientation Orientation)
{
    /// <summary>Finite cutting line in model coordinates; null preserves the legacy centered section.</summary>
    public SectionCut? Cut { get; init; }
    public ViewGraphicsSettings? Graphics { get; init; }

    /// <summary>Persistent room labels of this plan view; generated from the room model, not duplicated notes.</summary>
    public bool ShowRoomTags { get; init; }

    /// <summary>Persistent area labels of this plan view; generated from area objects.</summary>
    public bool ShowAreaTags { get; init; }

    /// <summary>Simple vertical visibility controls for plan views; null uses the architectural default.</summary>
    public PlanViewRange? Range { get; init; }

    /// <summary>Persistent plan marker and coverage for an elevation; null keeps automatic placement.</summary>
    public ElevationMarker? ElevationMarker { get; init; }

    /// <summary>Section discipline; structural mode retains walls, slabs, columns, foundations, beams, and connections.</summary>
    public SectionDisplayMode SectionMode { get; init; }

    /// <summary>
    /// The part of the drawing, in meters, that a plan, elevation or section prints on a sheet (its crop region): plan X
    /// and Y for a plan, horizontal position and height for an elevation or section, the isometric drawing axes for the
    /// 3D view. Null prints everything, which for a
    /// site with streets and gardens can shrink the building on the paper.
    /// </summary>
    public PlanBounds? ModelCrop { get; init; }

    /// <summary>Elements this view hides. The building itself is unchanged; other views still show them.</summary>
    public List<Guid> HiddenElementIds { get; init; } = [];

    /// <summary>When not empty, this view shows only these elements (and the openings in the walls shown).</summary>
    public List<Guid> IsolatedElementIds { get; init; } = [];

    /// <summary>Categories this view hides altogether, including elements of those categories added later.</summary>
    public List<ViewCategory> HiddenCategories { get; init; } = [];

    /// <summary>Rules that hide the walls they match in this view.</summary>
    public List<ViewFilter> Filters { get; init; } = [];

    /// <summary>The phase this view is drawn at; null draws the latest phase of the project.</summary>
    public Guid? PhaseId { get; init; }

    /// <summary>Which elements of the view's phase it shows: all, only new, only existing or only demolished.</summary>
    public PhaseFilter PhaseFilter { get; init; }

    /// <summary>The design options this view shows instead of the primary option of their sets.</summary>
    public List<OptionChoice> OptionChoices { get; init; } = [];
}

/// <summary>Creates the standard coordinated views included in every project.</summary>
public static class BuiltInProjectViews
{
    public static readonly Guid Model3DId = Guid.Parse("50802fa4-c8b4-44aa-a29f-68433e489130");
    public static readonly Guid NorthElevationId = Guid.Parse("03adbfd6-9c7d-4635-9f93-2645f1ac45e4");
    public static readonly Guid SectionAId = Guid.Parse("26870a61-1d20-4f66-918a-6138d5370687");
    public static readonly Guid ProjectScheduleId = Guid.Parse("54af0a0e-63f6-4fd8-9ff3-aa27947758e8");

    /// <summary>The area table view: rooms grouped by level, placeable on a sheet.</summary>
    public static readonly Guid AreaScheduleId = Guid.Parse("8d2f6a14-3b7e-4c59-a1d0-5e9c7b3f2a68");

    /// <summary>The location plan view: plot, street, neighbours and north, placeable on a sheet.</summary>
    public static readonly Guid LocationId = Guid.Parse("b41c9e07-6d25-4f83-8a1e-3c7d5f9b0e12");

    /// <summary>Whether a view is a generated drawing that sheets can place without a model scale of its own.</summary>
    public static bool IsGeneratedSheetView(ProjectView view) => view.Id == AreaScheduleId || view.Id == LocationId;

    public static readonly Guid SouthElevationId = Guid.Parse("7a1f0c52-5d4b-4f0e-8a9c-2b6e1d3f4a05");
    public static readonly Guid EastElevationId = Guid.Parse("c3d8e1b7-92a4-4c6f-b5e0-0f7a2d9c4b16");
    public static readonly Guid WestElevationId = Guid.Parse("e5b9a0d4-1c37-48fa-9d62-6a3c8f1e7b27");

    /// <summary>Default names of the standard views, as a Spanish Revit template calls them.</summary>
    public const string Model3DName = "{3D}", SectionName = "Sección 1", ScheduleName = "Tabla de cantidades";

    /// <summary>Names the four standard elevations carry in a new project; the user may rename any of them.</summary>
    public static readonly IReadOnlyDictionary<ViewOrientation, string> ElevationNames = new Dictionary<ViewOrientation, string>
    {
        [ViewOrientation.North] = "Vista Norte", [ViewOrientation.South] = "Vista Sur",
        [ViewOrientation.East] = "Vista Este", [ViewOrientation.West] = "Vista Oeste"
    };

    /// <summary>Creates plan views for all levels and the standard project-wide views.</summary>
    public static List<ProjectView> CreateDefaults(IReadOnlyCollection<Level> levels)
    {
        var views = levels.Select(CreatePlan).ToList();
        views.Add(new ProjectView(Model3DId, Model3DName, ProjectViewKind.Model3D, null, ViewOrientation.None));
        views.Add(new ProjectView(SectionAId, SectionName, ProjectViewKind.Section, null, ViewOrientation.East));
        views.Add(new ProjectView(ProjectScheduleId, ScheduleName, ProjectViewKind.Schedule, null, ViewOrientation.None));
        EnsureElevations(views);
        EnsureAreaSchedule(views);
        EnsureLocation(views);
        return views;
    }

    /// <summary>Adds the location plan view to projects made before it existed.</summary>
    public static void EnsureLocation(List<ProjectView> views)
    {
        if (views.All(view => view.Id != LocationId))
            views.Add(new ProjectView(LocationId, DefaultLabels.Get("Location.Title"), ProjectViewKind.Schedule, null,
                ViewOrientation.None));
    }

    /// <summary>Adds the area table view to projects made before it existed.</summary>
    public static void EnsureAreaSchedule(List<ProjectView> views)
    {
        if (views.All(view => view.Id != AreaScheduleId))
            views.Add(new ProjectView(AreaScheduleId, DefaultLabels.Get("AreaTable.Title"), ProjectViewKind.Schedule, null,
                ViewOrientation.None));
    }

    /// <summary>
    /// Makes sure the four elevations exist. Projects made before south, east and west elevations were offered get them;
    /// the old, unrenamed "North Elevation" takes its new name. A name the user chose is never touched.
    /// </summary>
    public static void EnsureElevations(List<ProjectView> views)
    {
        var ids = new Dictionary<ViewOrientation, Guid>
        {
            [ViewOrientation.North] = NorthElevationId, [ViewOrientation.South] = SouthElevationId,
            [ViewOrientation.East] = EastElevationId, [ViewOrientation.West] = WestElevationId
        };
        foreach (var (orientation, id) in ids)
        {
            var index = views.FindIndex(view => view.Id == id);
            if (index < 0)
                views.Add(new ProjectView(id, ElevationNames[orientation], ProjectViewKind.Elevation, null, orientation));
            else if (views[index].Name == "North Elevation")
                views[index] = views[index] with { Name = ElevationNames[orientation] };
        }
        // Projects made before the views were named in Spanish keep any name the user chose; only the untouched
        // English defaults are translated.
        var englishDefaults = new Dictionary<Guid, (string Old, string New)>
        {
            [Model3DId] = ("Coordinated 3D", Model3DName), [SectionAId] = ("Section A", SectionName),
            [ProjectScheduleId] = ("Project Schedule", ScheduleName)
        };
        for (var index = 0; index < views.Count; index++)
            if (englishDefaults.TryGetValue(views[index].Id, out var names) && views[index].Name == names.Old)
                views[index] = views[index] with { Name = names.New };
    }

    /// <summary>Creates the coordinated plan view owned by one level; like Revit, it carries the level's name.</summary>
    public static ProjectView CreatePlan(Level level) => new(
        Guid.NewGuid(),
        level.Name,
        ProjectViewKind.Plan,
        level.Id,
        ViewOrientation.None)
    {
        Range = PlanViewRange.Architectural
    };
}
