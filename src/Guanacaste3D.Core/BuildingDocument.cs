using System.Text.Json.Serialization;

namespace Guanacaste3D.Core;

/// <summary>Represents a position in the model's XY ground plane, measured in meters.</summary>
public readonly record struct Point2(double X, double Y)
{
    /// <summary>Returns the Euclidean metric distance to another point.</summary>
    public double DistanceTo(Point2 other) => double.Hypot(other.X - X, other.Y - Y);
}

/// <summary>A named vertical building datum, measured in meters from the project origin.</summary>
public sealed record Level(Guid Id, string Name, double Elevation)
{
    /// <summary>Protected base roles survive renaming and elevation changes.</summary>
    public LevelRole Role { get; init; }
}

/// <summary>A straight metric wall, tied to a building level.</summary>
public sealed record Wall(Guid Id, Guid LevelId, Point2 Start, Point2 End, double Thickness, double Height)
{
    public Guid WallTypeId { get; init; } = BuiltInWallTypes.Basic200Id;

    /// <summary>Optional reference datum; deleting either side never deletes the other.</summary>
    public Guid? GridAxisId { get; init; }

    /// <summary>Explicit opt-out so a detached wall is never reattached by geometric legacy inference.</summary>
    public bool GridAxisAssociationDisabled { get; init; }

    /// <summary>Level the wall top is tied to; null means the height is unconnected.</summary>
    public Guid? TopLevelId { get; init; }

    /// <summary>Distance in meters from the top level to the wall top (negative is below it).</summary>
    public double TopOffset { get; init; }

    /// <summary>Meters from the level elevation to the wall base: positive lifts the base above the level, negative takes the wall down (to a footing, say).</summary>
    public double BaseOffset { get; init; }

    /// <summary>The height of the wall from its base to its top.</summary>
    [JsonIgnore] public double NetHeight => Height - BaseOffset;

    /// <summary>Finish applied to both faces of the wall, used for finish area takeoff.</summary>
    public FinishKind Finish { get; init; }

    /// <summary>
    /// The plane that stays put when the wall's thickness changes. <c>Start</c> and <c>End</c> always describe the
    /// centerline; this records which plane the user placed the wall by. Exterior is the face on the right looking
    /// from start to end, interior the one on the left.
    /// </summary>
    public WallLocationReference LocationReference { get; init; }

    /// <summary>How the start of the wall treats the wall it meets; a corner uses the settings of both walls.</summary>
    public WallJoinSetting StartJoin { get; init; }

    /// <summary>How the end of the wall treats the wall it meets.</summary>
    public WallJoinSetting EndJoin { get; init; }

    /// <summary>The curved wall this straight piece belongs to; null for an ordinary wall.</summary>
    public Guid? CurveGroupId { get; init; }

    /// <summary>Radius of the arc the piece follows, in meters; zero for an ordinary wall.</summary>
    public double CurveRadius { get; init; }

    /// <summary>A pinned wall cannot be moved, rotated, mirrored in place, trimmed, aligned or deleted.</summary>
    public bool Pinned { get; init; }

    /// <summary>
    /// The shape of the wall's top, as points of (distance along the wall, height); null leaves it flat at
    /// <see cref="Height"/>. The list is never changed after it is set: a changed profile is a new list.
    /// </summary>
    public List<Point2>? TopProfile { get; init; }

    /// <summary>The height of the wall's top at a distance along it, following its profile and never above its height.</summary>
    public double TopHeightAt(double along) =>
        TopProfile is { Count: > 1 } profile ? Math.Min(Height, WallProfiles.HeightAt(profile, along)) : Height;

    /// <summary>The area of the wall's face in elevation, in square meters, before the openings are taken out.</summary>
    [JsonIgnore]
    public double ElevationArea
    {
        get
        {
            if (TopProfile is not { Count: > 1 } profile) return Length * NetHeight;
            var stations = profile.Select(point => point.X).Where(x => x > 0 && x < Length).Append(0).Append(Length).Order().ToArray();
            var area = 0.0;
            for (var index = 1; index < stations.Length; index++)
                area += (stations[index] - stations[index - 1]) * (TopHeightAt(stations[index - 1]) + TopHeightAt(stations[index])) / 2;
            return Math.Max(0, area - Length * BaseOffset);
        }
    }

    [JsonIgnore] public double Length => Start.DistanceTo(End);
}

/// <summary>Owns the editable architectural source of truth for all 2D and 3D views.</summary>
public sealed class BuildingDocument
{
    /// <summary>The schema written by this build; older files are migrated on load.</summary>
    public const int CurrentSchemaVersion = 15;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled Project";
    public List<PlanDimension> Dimensions { get; set; } = [];
    public List<Callout> Callouts { get; set; } = [];
    public List<Level> Levels { get; set; } = [];
    public List<GridAxis> Grids { get; set; } = [];
    public List<Wall> Walls { get; set; } = [];
    public List<WallOpening> Openings { get; set; } = [];
    public List<WallType> WallTypes { get; set; } = [];
    public List<OpeningType> OpeningTypes { get; set; } = [];
    public List<Room> Rooms { get; set; } = [];
    /// <summary>Non-solid curved separators participating in room enclosure detection.</summary>
    public List<CurvedRoomSeparator> CurvedRoomSeparators { get; set; } = [];
    public List<FloorSlab> Floors { get; set; } = [];
    public List<Column> Columns { get; set; } = [];
    public List<StructuralElement> StructuralElements { get; set; } = [];
    public List<DrawingSheet> Sheets { get; set; } = [];
    public List<FamilyDocument> SheetFamilies { get; set; } = [];
    public List<ProjectView> Views { get; set; } = [];
    public List<ViewTemplate> ViewTemplates { get; set; } = [];
    /// <summary>Named sets of elements: groups, assemblies and detail groups.</summary>
    public List<ElementGroup> ElementGroups { get; set; } = [];
    /// <summary>How the project shows and reads lengths, areas and volumes; the model itself is always in meters.</summary>
    public ProjectUnits Units { get; set; } = ProjectUnits.Default;
    /// <summary>Where the project is and how it is turned to true north; shared coordinates of its origin.</summary>
    public ProjectSite Site { get; set; } = ProjectSite.Default;
    /// <summary>Images and PDFs kept inside the project.</summary>
    public List<ReferenceFile> References { get; set; } = [];
    /// <summary>Parts of the building that one person can take for editing; empty until the project is shared.</summary>
    public List<Workset> Worksets { get; set; } = [];
    public List<WorksetAssignment> WorksetAssignments { get; set; } = [];
    /// <summary>Whether the project is shared through a central model, and by whom this copy is edited.</summary>
    public CollaborationSettings Collaboration { get; set; } = CollaborationSettings.Off;
    /// <summary>Which sheets and formats the project publishes, and where.</summary>
    public PublishSettings Publish { get; set; } = PublishSettings.Default;
    /// <summary>Cover, bars and steel of the reinforcement.</summary>
    public ReinforcementSettings Reinforcement { get; set; } = ReinforcementSettings.Default;
    /// <summary>How walls are divided into precast panels.</summary>
    public PrecastSettings Precast { get; set; } = PrecastSettings.Default;
    /// <summary>The concrete: its grade and weight.</summary>
    public ConcreteSettings Concrete { get; set; } = ConcreteSettings.Default;
    /// <summary>Elements whose cover is not the project's.</summary>
    public List<CoverOverride> CoverOverrides { get; set; } = [];
    /// <summary>Groups of rooms treated as one thermal zone.</summary>
    public List<Zone> Zones { get; set; } = [];
    /// <summary>The measure that colours the rooms in the plan; none leaves them as they are.</summary>
    public ResultsMetric ResultsMetric { get; set; }
    /// <summary>Studies that try shapes of a mass and rank them.</summary>
    public List<DesignStudy> Studies { get; set; } = [];
    /// <summary>Named blobs of text (usually JSON) that other features keep in the project, such as the render's animation keys. See <see cref="ProjectExtensions"/>.</summary>
    public Dictionary<string, string> Extensions { get; set; } = new();
    /// <summary>Saved sequences of commands that can be played again.</summary>
    public List<AutomationScript> Automations { get; set; } = [];
    /// <summary>Images laid in the plan of a level as drawings to trace over.</summary>
    public List<ImageUnderlay> Underlays { get; set; } = [];
    /// <summary>Scanned or surveyed points kept as a reference in the plan.</summary>
    public List<PointCloud> PointClouds { get; set; } = [];
    /// <summary>The stages of the building's life in order; empty until the project uses phases.</summary>
    public List<ProjectPhase> Phases { get; set; } = [];
    public List<PhaseAssignment> PhaseAssignments { get; set; } = [];
    /// <summary>Groups of alternatives for a part of the building; empty until the project uses design options.</summary>
    public List<DesignOptionSet> OptionSets { get; set; } = [];
    public List<OptionAssignment> OptionAssignments { get; set; } = [];
    public List<ParameterDefinition> ParameterDefinitions { get; set; } = [];
    public List<ParameterValue> ParameterValues { get; set; } = [];
    public List<ScheduleDefinition> Schedules { get; set; } = [];
    public List<FamilyDocument> InPlaceFamilies { get; set; } = [];
    /// <summary>Loaded mesh component families (.g3df) that planting and component instances refer to.</summary>
    public List<FamilyDocument> ComponentFamilies { get; set; } = [];
    /// <summary>The render view the project was last saved with (camera, sun, filters); null for the default view.</summary>
    public Rendering.RenderSetup? RenderSetup { get; set; }
    /// <summary>Named render cameras and settings retained with the project.</summary>
    public List<Rendering.RenderSceneBookmark> RenderScenes { get; set; } = [];
    public List<RenderMaterialAssignment> RenderMaterials { get; set; } = [];
    public List<ElectricalDevice> ElectricalDevices { get; set; } = [];
    public List<ElectricalCircuit> ElectricalCircuits { get; set; } = [];
    public List<PlumbingPoint> PlumbingPoints { get; set; } = [];
    public List<PlumbingSystem> PlumbingSystems { get; set; } = [];

    /// <summary>The plot for the location plan; null lets the plan propose one around the building.</summary>
    public SiteLot? Lot { get; set; }

    /// <summary>Creates a project with protected floor/roof datums and built-in architectural type catalogs.</summary>
    public static BuildingDocument Create(string name = "Untitled Project")
    {
        var levels = new List<Level>
        {
            new(Guid.NewGuid(), DefaultLabels.Get("LevelName", 1), 0) { Role = LevelRole.BaseFloor },
            new(Guid.NewGuid(), DefaultLabels.Get("LevelName", 2), 3) { Role = LevelRole.BaseRoof }
        };
        return new BuildingDocument
        {
            Name = name,
            Levels = levels,
            WallTypes = BuiltInWallTypes.CreateDefaults(),
            OpeningTypes = BuiltInOpeningTypes.CreateDefaults(),
            Views = BuiltInProjectViews.CreateDefaults(levels)
        };
    }

    /// <summary>Returns a deep-enough copy because contained entities are immutable records.</summary>
    public BuildingDocument Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Id = Id,
        Name = Name,
        Dimensions = [.. Dimensions],
        Callouts = [.. Callouts ?? []],
        Levels = [.. Levels],
        Grids = [.. Grids],
        Walls = [.. Walls],
        Openings = [.. Openings],
        WallTypes = [.. WallTypes],
        OpeningTypes = [.. OpeningTypes],
        Rooms = Rooms.Select(room => room with
        {
            Boundary = room.Boundary is null ? null : [.. room.Boundary],
            Holes = room.Holes is null ? null : room.Holes.Select(ring => ring.ToList()).ToList()
        }).ToList(),
        CurvedRoomSeparators = [.. CurvedRoomSeparators],
        Floors = Floors.Select(floor => floor with
        {
            Boundary = [.. floor.Boundary], TopoPoints = floor.TopoPoints is null ? null : [.. floor.TopoPoints]
        }).ToList(),
        Columns = [.. Columns],
        StructuralElements = [.. StructuralElements],
        Sheets = Sheets.Select(sheet => sheet with { Viewports = [.. sheet.Viewports] }).ToList(),
        SheetFamilies = SheetFamilies.Select(family => family.Clone()).ToList(),
        Views = [.. Views],
        ViewTemplates = [.. ViewTemplates],
        Units = Units,
        Site = Site,
        References = [.. References],
        Worksets = [.. Worksets],
        WorksetAssignments = [.. WorksetAssignments],
        Collaboration = Collaboration,
        Publish = Publish with { SheetIds = [.. Publish.SheetIds] },
        Reinforcement = Reinforcement,
        Precast = Precast,
        Concrete = Concrete,
        CoverOverrides = [.. CoverOverrides],
        Zones = Zones.Select(zone => zone with { RoomIds = [.. zone.RoomIds] }).ToList(),
        ResultsMetric = ResultsMetric,
        Studies = [.. Studies],
        Extensions = new Dictionary<string, string>(Extensions),
        Automations = Automations.Select(script => script with { Steps = script.Steps.Select(step => step with { Answers = [.. step.Answers] }).ToList() }).ToList(),
        Underlays = [.. Underlays],
        PointClouds = [.. PointClouds],
        ElementGroups = ElementGroups.Select(group => group with { MemberIds = [.. group.MemberIds] }).ToList(),
        Phases = [.. Phases],
        PhaseAssignments = [.. PhaseAssignments],
        OptionSets = OptionSets.Select(set => set with { Options = [.. set.Options] }).ToList(),
        OptionAssignments = [.. OptionAssignments],
        ParameterDefinitions = ParameterDefinitions
            .Select(definition => definition with { Categories = [.. definition.Categories] }).ToList(),
        ParameterValues = [.. ParameterValues],
        Schedules = Schedules.Select(schedule => schedule with
        {
            Fields = [.. schedule.Fields], Filters = [.. schedule.Filters], SortGroups = [.. schedule.SortGroups]
        }).ToList(),
        InPlaceFamilies = InPlaceFamilies.Select(family => family.Clone()).ToList(),
        ComponentFamilies = ComponentFamilies.Select(family => family.Clone()).ToList(),
        RenderSetup = RenderSetup,
        RenderScenes = [.. RenderScenes],
        RenderMaterials = [.. RenderMaterials],
        ElectricalDevices = ElectricalDevices.Select(device => device with { Controls = [.. device.Controls] }).ToList(),
        ElectricalCircuits = [.. ElectricalCircuits],
        PlumbingPoints = [.. PlumbingPoints],
        PlumbingSystems = PlumbingSystems.Select(system => system with { Fixtures = [.. system.Fixtures] }).ToList(),
        Lot = Lot
    };

    private void ElementGroups_Validate() => Guanacaste3D.Core.ElementGroups.Validate(this);

    /// <summary>Rejects inconsistent projects before mutation, opening, or writing a file.</summary>
    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion) throw new InvalidDataException("Unsupported project schema version.");
        foreach (var view in Views) view.Graphics?.Validate();
        ProjectStages.Validate(this);
        ElementGroups_Validate();
        ReferenceFiles.Validate(this);
        if (Collaboration is null || !Collaboration.IsValid || Worksets is null || WorksetAssignments is null || Worksets.Count > 200 ||
            Worksets.Any(item => item is null || item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 120 || (item.Owner is not null && item.Owner.Length > 60)) ||
            Worksets.Select(item => item.Id).Distinct().Count() != Worksets.Count || WorksetAssignments.Select(item => item.Id).Distinct().Count() != WorksetAssignments.Count)
            throw new InvalidDataException("Invalid worksets.");
        if (Publish is null || !Publish.IsValid) throw new InvalidDataException("The publish settings are invalid.");
        if (Reinforcement is null || !Reinforcement.IsValid) throw new InvalidDataException("The reinforcement settings are invalid.");
        if (Precast is null || !Precast.IsValid) throw new InvalidDataException("The precast settings are invalid.");
        if (Concrete is null || !Concrete.IsValid) throw new InvalidDataException("The concrete settings are invalid.");
        if (Zones is null || Zones.Count > 500 || !Enum.IsDefined(ResultsMetric) ||
            Zones.Any(zone => zone is null || zone.Id == Guid.Empty || string.IsNullOrWhiteSpace(zone.Name) || zone.Name.Length > 120 || zone.RoomIds is null || zone.RoomIds.Count == 0) ||
            Zones.Select(zone => zone.Id).Distinct().Count() != Zones.Count)
            throw new InvalidDataException("Invalid zones.");
        if (Studies is null || Studies.Count > DesignStudies.MaximumStudies || Studies.Any(study => study is null || !study.IsValid) || Studies.Select(study => study.Id).Distinct().Count() != Studies.Count ||
            Automations is null || Automations.Count > AutomationScripts.MaximumScripts || Automations.Any(script => !AutomationScripts.IsValid(script)) || Automations.Select(script => script.Id).Distinct().Count() != Automations.Count)
            throw new InvalidDataException("Invalid studies or scripts.");
        if (!ProjectExtensions.IsValid(Extensions)) throw new InvalidDataException("Invalid project extensions.");
        if (CoverOverrides is null || CoverOverrides.Count > 100000 || CoverOverrides.Any(item => item is null || item.Cover is < 0.01 or > 0.15) || CoverOverrides.Select(item => item.Id).Distinct().Count() != CoverOverrides.Count)
            throw new InvalidDataException("The cover is invalid.");
        if (Units is null || !Units.IsValid) throw new InvalidDataException("The project units are invalid.");
        if (Site is null || !Site.IsValid) throw new InvalidDataException("The project site is invalid.");
        if (Views.Any(view => view.ModelCrop is { } crop && (!double.IsFinite(crop.MinX + crop.MinY + crop.MaxX + crop.MaxY) ||
                crop.MaxX - crop.MinX < 0.5 || crop.MaxY - crop.MinY < 0.5 ||
                view.Kind is ProjectViewKind.Schedule)))
            throw new InvalidDataException("The view crop region is invalid.");
        if (Lot is not null && !Lot.IsValid) throw new InvalidDataException("The site lot is invalid.");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 120)
            throw new InvalidDataException("The project identity or name is invalid.");
        if (Dimensions is null || Levels is null || Grids is null || Walls is null || Openings is null || WallTypes is null ||
            Rooms is null || CurvedRoomSeparators is null || Floors is null || Columns is null || StructuralElements is null ||
            Sheets is null || Views is null ||
            Levels.Count is < 1 or > 200 || Walls.Count > 100000 || Openings.Count > 200000 ||
            Rooms.Count > 100000 || CurvedRoomSeparators.Count > 10000 ||
            Floors.Count > 100000 || Columns.Count > 100000 ||
            StructuralElements.Count > 100000 ||
            Sheets.Count > 10000 || Views.Count > 10000)
            throw new InvalidDataException("The document exceeds the supported element limits.");
        if (Levels.Any(l => l is null))
            throw new InvalidDataException("A level cannot be null.");
        if (Levels.Any(l => l.Id == Guid.Empty || string.IsNullOrWhiteSpace(l.Name) ||
             l.Name.Length > 100 || !double.IsFinite(l.Elevation)) ||
            Levels.Select(l => l.Id).Distinct().Count() != Levels.Count)
            throw new InvalidDataException("The level collection is invalid.");
        var levelIds = Levels.Select(l => l.Id).ToHashSet();
        BaseLevels.Validate(this);
        if (Grids.Count > 10000 || Grids.Any(axis => axis is null || !axis.IsValid) ||
            // Match the interactive editor's case-insensitive, whitespace-trimmed axis-name rule.
            // Imported/native documents must not bypass uniqueness by storing "A" and " A ".
            Grids.Select(axis => axis.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Grids.Count ||
            Grids.Select(axis => axis.Id).Distinct().Count() != Grids.Count)
            throw new InvalidDataException("Invalid grid axis definitions.");
        // Resolve references once. Checking each wall against Grids.Any() used to scale as
        // O(walls × grids), which is costly in large architectural projects.
        var gridsById = Grids.ToDictionary(axis => axis.Id);
        if (WallTypes.Count is < 1 or > 10000 || WallTypes.Any(type =>
                type.Id == Guid.Empty || string.IsNullOrWhiteSpace(type.Name) || type.Name.Length > 120 ||
                !Enum.IsDefined(type.Function) || !double.IsFinite(type.Thickness) ||
                type.Thickness is < 0.03 or > 5 || string.IsNullOrWhiteSpace(type.MaterialName) ||
                type.MaterialName.Length > 120 || !Enum.IsDefined(type.PlanFillPattern) ||
                !Enum.IsDefined(type.SectionFillPattern) || !IsRgbColor(type.PlanFillColor) ||
                !IsRgbColor(type.SectionFillColor) ||
                !Enum.IsDefined(type.Construction) ||
                !double.IsFinite(type.LeftRepelloThickness) || type.LeftRepelloThickness is < 0 or > 0.05 ||
                !double.IsFinite(type.RightRepelloThickness) || type.RightRepelloThickness is < 0 or > 0.05 ||
                !double.IsFinite(type.LeftFineThickness) || type.LeftFineThickness is < 0 or > 0.02 ||
                !double.IsFinite(type.RightFineThickness) || type.RightFineThickness is < 0 or > 0.02 ||
                type.LeftRepelloThickness + type.LeftFineThickness + type.RightRepelloThickness + type.RightFineThickness >= type.Thickness ||
                !double.IsFinite(type.GypsumStudSpacing) || type.GypsumStudSpacing is < 0.15 or > 1.2) ||
            WallTypes.Select(type => type.Id).Distinct().Count() != WallTypes.Count)
            throw new InvalidDataException("Invalid wall-type catalog.");
        var wallTypeIds = WallTypes.Select(type => type.Id).ToHashSet();
        if (Walls.Any(w => w is null) || Walls.Select(w => w.Id).Distinct().Count() != Walls.Count)
            throw new InvalidDataException("The wall collection contains duplicate identifiers.");
        foreach (var wall in Walls)
        {
            if (wall.GridAxisId is Guid linkedAxis &&
                (!gridsById.TryGetValue(linkedAxis, out var datum) ||
                 !datum.IsCollinearWith(wall.Start, wall.End)))
                throw new InvalidDataException("A wall references a missing or non-collinear grid axis.");
            if (wall.Id == Guid.Empty || !levelIds.Contains(wall.LevelId) || !wallTypeIds.Contains(wall.WallTypeId) ||
                !double.IsFinite(wall.Start.X) || !double.IsFinite(wall.Start.Y) ||
                !double.IsFinite(wall.End.X) || !double.IsFinite(wall.End.Y) ||
                (wall.TopProfile is not null && !WallProfiles.IsValid(wall.TopProfile)) ||
                !double.IsFinite(wall.Thickness) || !double.IsFinite(wall.Height) ||
                wall.Length < 0.05 || wall.Length > 100000 ||
                wall.Thickness is < 0.05 or > 5 || wall.Height is < 0.1 or > 1000 || !double.IsFinite(wall.BaseOffset) || wall.BaseOffset < -50 || wall.BaseOffset > wall.Height - 0.1 ||
                !Enum.IsDefined(wall.Finish) || !Enum.IsDefined(wall.LocationReference) ||
                !Enum.IsDefined(wall.StartJoin) || !Enum.IsDefined(wall.EndJoin) ||
                !double.IsFinite(wall.CurveRadius) || wall.CurveRadius < 0 || (wall.CurveGroupId is null && wall.CurveRadius != 0))
                throw new InvalidDataException("A wall is invalid or has no associated level.");
        }
        ValidateRooms(levelIds);
        if (CurvedRoomSeparators.Any(arc => arc is null || !arc.IsValid || !levelIds.Contains(arc.LevelId)))
            throw new InvalidDataException("Invalid curved room separators.");
        ValidateFloors(levelIds);
        ValidateColumns(levelIds);
        ValidateSheets();
        DocumentSession.ValidateSheetLayouts(this);
        ValidateViews(levelIds);
        if (Dimensions.Any(item => item is null) || StructuralElements.Any(item => item is null))
            throw new InvalidDataException("Invalid plan dimension references.");
        var allIds = Levels.Select(level => level.Id)
            .Concat(Grids.Select(axis => axis.Id))
            .Concat(Walls.Select(wall => wall.Id))
            .Concat(Openings.Select(opening => opening.Id))
            .Concat(WallTypes.Select(type => type.Id))
            .Concat(Rooms.Select(room => room.Id))
            .Concat(CurvedRoomSeparators.Select(arc => arc.Id))
            .Concat(Floors.Select(floor => floor.Id))
            .Concat(Columns.Select(column => column.Id))
            .Concat(Dimensions.Select(dimension => dimension.Id))
            .Concat((Callouts ?? []).Where(callout => callout is not null).Select(callout => callout.Id))
            .Concat(StructuralElements.Select(element => element.Id))
            .Concat(Sheets.Select(sheet => sheet.Id))
            .Concat(Sheets.SelectMany(sheet => sheet.Viewports).Select(viewport => viewport.Id))
            .Concat(SheetFamilies.Select(family => family.Id))
            .Concat(Views.Select(view => view.Id))
            .Concat((ParameterDefinitions ?? []).Select(definition => definition.Id))
            .Concat((OpeningTypes ?? []).Select(type => type.Id))
            .Concat((Schedules ?? []).Select(schedule => schedule.Id))
            .Concat((InPlaceFamilies ?? []).Where(family => family is not null).Select(family => family.Id))
            .Concat((ComponentFamilies ?? []).Where(family => family is not null).Select(family => family.Id))
            .ToArray();
        if (Openings.Any(o => o is null) || allIds.Distinct().Count() != allIds.Length)
            throw new InvalidDataException("Element identifiers must be unique.");
        WallGeometry.Validate(Walls, Openings);
        PlanDimensions.Validate(this);
        CalloutRules.Validate(this);
        CirculationGeometry.Validate(this);
        ArchitecturalShapes.Validate(this);
        OpeningTypeRules.Validate(this);
        LevelConstraints.Validate(this);
        ParameterRules.Validate(this);
        ScheduleDefinitionRules.Validate(this);
        InPlaceFamilyRules.Validate(this);
        ComponentFamilyRules.Validate(this);
        RenderSetup?.Validate();
        if (RenderScenes is null || RenderScenes.Count > 1000 ||
            RenderScenes.Any(scene => scene is null || scene.Id == Guid.Empty || string.IsNullOrWhiteSpace(scene.Name) || scene.Name.Length > 120 || scene.Setup is null || (scene.Thumbnail is not null && scene.Thumbnail.Length > Rendering.RenderSceneBookmark.MaximumThumbnailLength)) ||
            RenderScenes.Select(scene => scene.Id).Distinct().Count() != RenderScenes.Count)
            throw new InvalidDataException("The render scene collection is invalid.");
        foreach (var scene in RenderScenes) scene.Setup.Validate();
        RenderMaterialRules.Validate(this);
        ElectricalRules.Validate(this);
        PlumbingRules.Validate(this);
    }

    /// <summary>Checks the persisted color contract without coupling the core model to a UI framework.</summary>
    private static bool IsRgbColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    /// <summary>Validates room identity, level ownership, dimensions, and labels.</summary>
    private void ValidateRooms(IReadOnlySet<Guid> levelIds)
    {
        if (Rooms.Any(room => room.Id == Guid.Empty || !levelIds.Contains(room.LevelId) ||
                string.IsNullOrWhiteSpace(room.Number) || room.Number.Length > 32 ||
                string.IsNullOrWhiteSpace(room.Name) || room.Name.Length > 120 ||
                !double.IsFinite(room.FirstCorner.X) || !double.IsFinite(room.FirstCorner.Y) ||
                !double.IsFinite(room.SecondCorner.X) || !double.IsFinite(room.SecondCorner.Y) ||
                (room.Boundary is { } outline && !RoomRegionGeometry.ValidRegion(
                    outline, room.Holes?.Select(hole => (IReadOnlyList<Point2>)hole).ToArray() ?? [])) ||
                (room.Boundary is null && room.Holes is { Count: > 0 }) ||
                room.Area is < 0.1 or > 100000000) ||
            Rooms.Select(room => room.Number).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Rooms.Count)
            throw new InvalidDataException("Invalid room definitions.");
    }

    /// <summary>Validates slab boundaries, dimensions, and level ownership.</summary>
    private void ValidateFloors(IReadOnlySet<Guid> levelIds)
    {
        foreach (var floor in Floors)
        {
            if (floor.Id == Guid.Empty || !levelIds.Contains(floor.LevelId) ||
                string.IsNullOrWhiteSpace(floor.Name) || floor.Name.Length > 120 ||
                floor.Boundary is null || floor.Boundary.Count is < 3 or > 500 ||
                floor.Boundary.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)) ||
                !double.IsFinite(floor.Thickness) || floor.Thickness > 3 ||
                floor.Thickness < (floor.Kind == FloorKind.Ceiling ? 0.005 : 0.03) ||
                !double.IsFinite(floor.ElevationOffset) || floor.ElevationOffset is < -10 or > 1000 ||
                !Enum.IsDefined(floor.Kind) ||
                floor.CalculateArea() is < 0.1 or > 100000000 || !Enum.IsDefined(floor.Finish) ||
                !double.IsFinite(floor.Overhang) || floor.Overhang is < 0 or > 5 ||
                (floor.Overhang > 0 && floor.Kind != FloorKind.Roof))
                throw new InvalidDataException("Invalid floor slab.");
            if (floor.TopoPoints is { } points &&
                (!floor.IsToposolid || points.Count > 2000 || points.Any(point =>
                    !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Elevation) ||
                    point.Elevation is < -1000 or > 5000 ||
                    !Geometry.TopographyGeometry.Contains(floor.Boundary, point.Plan) &&
                    floor.Boundary.All(corner => corner.DistanceTo(point.Plan) > 0.05))))
                throw new InvalidDataException("Invalid toposolid point.");
        }
    }

    /// <summary>Validates column identity, level ownership, plan section, and height.</summary>
    private void ValidateColumns(IReadOnlySet<Guid> levelIds)
    {
        if (Columns.Any(column => column.Id == Guid.Empty || !levelIds.Contains(column.LevelId) ||
                !double.IsFinite(column.Position.X) || !double.IsFinite(column.Position.Y) ||
                !double.IsFinite(column.Width) || column.Width is < 0.05 or > 10 ||
                !double.IsFinite(column.Depth) || column.Depth is < 0.05 or > 10 ||
                !double.IsFinite(column.Height) || column.Height is < 0.1 or > 1000) ||
            Columns.Select(column => column.Id).Distinct().Count() != Columns.Count)
            throw new InvalidDataException("Invalid column definitions.");
    }

    /// <summary>Validates sheet identity, numbering, names, and paper sizes.</summary>
    private void ValidateSheets()
    {
        if (Sheets.Any(sheet => sheet.Id == Guid.Empty || string.IsNullOrWhiteSpace(sheet.Number) ||
                sheet.Number.Length > 32 || string.IsNullOrWhiteSpace(sheet.Name) ||
                sheet.Name.Length > 120 || !Enum.IsDefined(sheet.PaperSize)) ||
            Sheets.Select(sheet => sheet.Number).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Sheets.Count)
            throw new InvalidDataException("Invalid drawing sheets.");
    }

    /// <summary>Validates view identity, naming, kind, orientation, and level ownership.</summary>
    private void ValidateViews(IReadOnlySet<Guid> levelIds)
    {
        if (Views.Count < 1 || Views.Any(view => view.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(view.Name) || view.Name.Length > 120 ||
                !Enum.IsDefined(view.Kind) || !Enum.IsDefined(view.Orientation) ||
                !Enum.IsDefined(view.SectionMode) ||
                view.HiddenCategories is null || view.HiddenCategories.Any(category => !Enum.IsDefined(category)) ||
                view.Filters is null || view.Filters.Count > DocumentSession.MaximumViewFilters ||
                view.Filters.Any(filter => filter is null || !filter.IsValid) ||
                (view.Kind != ProjectViewKind.Section && view.SectionMode != SectionDisplayMode.Coordinated) ||
                (view.Range is not null && (view.Kind != ProjectViewKind.Plan || !view.Range.IsValid)) ||
                (view.ElevationMarker is not null &&
                    (view.Kind != ProjectViewKind.Elevation || !view.ElevationMarker.IsValid)) ||
                (view.Cut is not null && (view.Kind != ProjectViewKind.Section || !view.Cut.IsValid)) ||
                (view.Kind == ProjectViewKind.Plan &&
                    (view.LevelId is not Guid levelId || !levelIds.Contains(levelId))) ||
                (view.Kind != ProjectViewKind.Plan && view.LevelId is not null)) ||
            Views.Select(view => view.Id).Distinct().Count() != Views.Count)
            throw new InvalidDataException("Invalid project views.");
    }
}
