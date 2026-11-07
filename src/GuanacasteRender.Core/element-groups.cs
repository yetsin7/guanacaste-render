using System.Text.Json;

namespace Guanacaste3D.Core;

/// <summary>What a named set of elements is for.</summary>
public enum ElementGroupKind
{
    /// <summary>Elements that are moved, copied and deleted together, and can be saved to a file to load into other projects.</summary>
    Group,

    /// <summary>Elements gathered to be documented as one part, such as a prefabricated wall. They are edited as usual.</summary>
    Assembly,

    /// <summary>Drafting elements (detail lines, regions, clouds, insulation, notes) that go together.</summary>
    Detail
}

/// <summary>A named set of walls, openings, slabs, columns and structural elements.</summary>
public sealed record ElementGroup(Guid Id, string Name, ElementGroupKind Kind, List<Guid> MemberIds);

/// <summary>Rules and clean-up shared by every kind of element group.</summary>
public static class ElementGroups
{
    public const int MaximumGroups = 2000;
    public const int MaximumMembers = 100000;

    /// <summary>The drafting kinds a detail group may hold.</summary>
    public static bool IsDrafting(StructuralElementKind kind) => kind is StructuralElementKind.DetailLine or StructuralElementKind.FilledRegion
        or StructuralElementKind.RevisionCloud or StructuralElementKind.Insulation or StructuralElementKind.ModelText;

    /// <summary>Whether the document has an element of any kind with this identifier.</summary>
    public static bool Exists(BuildingDocument document, Guid id) =>
        document.Walls.Any(item => item.Id == id) || document.Openings.Any(item => item.Id == id) ||
        document.Floors.Any(item => item.Id == id) || document.Columns.Any(item => item.Id == id) ||
        document.StructuralElements.Any(item => item.Id == id);

    /// <summary>Takes out the members that no longer exist, and the groups that are left empty.</summary>
    public static void Prune(BuildingDocument document)
    {
        if (document.ElementGroups.Count == 0) return;
        var alive = document.Walls.Select(item => item.Id).Concat(document.Openings.Select(item => item.Id))
            .Concat(document.Floors.Select(item => item.Id)).Concat(document.Columns.Select(item => item.Id))
            .Concat(document.StructuralElements.Select(item => item.Id)).ToHashSet();
        for (var index = document.ElementGroups.Count - 1; index >= 0; index--)
        {
            var group = document.ElementGroups[index];
            if (group.MemberIds.All(alive.Contains)) continue;
            var kept = group.MemberIds.Where(alive.Contains).ToList();
            if (kept.Count == 0) document.ElementGroups.RemoveAt(index);
            else document.ElementGroups[index] = group with { MemberIds = kept };
        }
    }

    /// <summary>Whether two kinds compete for their members: an element is in one group or detail group, and in one assembly.</summary>
    public static bool Competes(ElementGroupKind first, ElementGroupKind second) =>
        (first == ElementGroupKind.Assembly) == (second == ElementGroupKind.Assembly);

    public static void Validate(BuildingDocument document)
    {
        if (document.ElementGroups is null || document.ElementGroups.Count > MaximumGroups ||
            document.ElementGroups.Any(group => group is null || group.Id == Guid.Empty || string.IsNullOrWhiteSpace(group.Name) ||
                group.Name.Length > 120 || !Enum.IsDefined(group.Kind) || group.MemberIds is null || group.MemberIds.Count is < 1 or > MaximumMembers ||
                group.MemberIds.Distinct().Count() != group.MemberIds.Count) ||
            document.ElementGroups.Select(group => group.Id).Distinct().Count() != document.ElementGroups.Count ||
            document.ElementGroups.GroupBy(group => (group.Kind, group.Name.Trim().ToUpperInvariant())).Any(entry => entry.Count() > 1))
            throw new InvalidDataException("Invalid element groups.");
    }
}

/// <summary>What a group saved to a file holds: its elements and the wall and opening types they refer to.</summary>
public sealed record ElementGroupFileData(string Format, string Name, ElementGroupKind Kind, List<Wall> Walls, List<WallOpening> Openings,
    List<FloorSlab> Floors, List<Column> Columns, List<StructuralElement> StructuralElements, List<WallType> WallTypes, List<OpeningType> OpeningTypes);

public sealed partial class DocumentSession
{
    private const string GroupFileFormat = "G3D-GROUP/1";

    private static string CleanGroupName(string name)
    {
        var clean = name?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean.Length > 120) throw new InvalidDataException("The name is empty or too long.");
        return clean;
    }

    private string UniqueGroupName(string name, ElementGroupKind kind)
    {
        var taken = Document.ElementGroups.Where(group => group.Kind == kind).Select(group => group.Name.Trim().ToUpperInvariant()).ToHashSet();
        if (!taken.Contains(name.ToUpperInvariant())) return name;
        for (var number = 2; ; number++)
            if (!taken.Contains($"{name} {number}".ToUpperInvariant())) return $"{name} {number}";
    }

    /// <summary>
    /// Gathers elements into a group, assembly or detail group, as one undoable step. The doors and windows of the walls
    /// are part of them without being listed. An element can be in one group (or detail group) and in one assembly.
    /// </summary>
    public Guid CreateElementGroup(IReadOnlyCollection<Guid> ids, string name, ElementGroupKind kind)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("Invalid element groups.");
        var clean = CleanGroupName(name);
        var members = ids.Distinct().Where(id => ElementGroups.Exists(Document, id)).ToList();
        if (members.Count == 0) throw new InvalidDataException("Select the elements to gather first.");
        // The openings travel with their wall, so an opening listed along with its wall adds nothing.
        members.RemoveAll(id => Document.Openings.FirstOrDefault(opening => opening.Id == id) is { } hosted && members.Contains(hosted.WallId));
        if (Document.ElementGroups.Count >= ElementGroups.MaximumGroups) throw new InvalidDataException("A project cannot have so many groups.");
        if (Document.ElementGroups.Any(group => group.Kind == kind && group.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidDataException("A group with that name already exists.");
        if (Document.ElementGroups.Any(group => ElementGroups.Competes(group.Kind, kind) && group.MemberIds.Intersect(members).Any()))
            throw new InvalidDataException("An element is already in another group of this kind.");
        if (kind == ElementGroupKind.Detail && members.Any(id => Document.StructuralElements.FirstOrDefault(item => item.Id == id) is not { } element ||
                                                               !ElementGroups.IsDrafting(element.Kind)))
            throw new InvalidDataException("A detail group holds only drafting elements.");
        var groupId = Guid.NewGuid();
        Commit("Transaction.CreateElementGroup", document => document.ElementGroups.Add(new ElementGroup(groupId, clean, kind, members)));
        return groupId;
    }

    public void RenameElementGroup(Guid groupId, string name)
    {
        var clean = CleanGroupName(name);
        var group = Document.ElementGroups.FirstOrDefault(item => item.Id == groupId) ?? throw new InvalidDataException("The group does not exist.");
        if (Document.ElementGroups.Any(item => item.Id != groupId && item.Kind == group.Kind && item.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidDataException("A group with that name already exists.");
        Commit("Transaction.RenameElementGroup", document =>
        {
            var index = document.ElementGroups.FindIndex(item => item.Id == groupId);
            document.ElementGroups[index] = document.ElementGroups[index] with { Name = clean };
        });
    }

    /// <summary>Dissolves a group; its elements stay where they are, on their own.</summary>
    public void Ungroup(Guid groupId)
    {
        if (Document.ElementGroups.All(item => item.Id != groupId)) throw new InvalidDataException("The group does not exist.");
        Commit("Transaction.Ungroup", document => document.ElementGroups.RemoveAll(item => item.Id == groupId));
    }

    private ElementGroup RequireGroup(Guid groupId) =>
        Document.ElementGroups.FirstOrDefault(item => item.Id == groupId) ?? throw new InvalidDataException("The group does not exist.");

    /// <summary>The ids of every element in the group, counting the openings of its walls.</summary>
    public IReadOnlyList<Guid> GroupElements(Guid groupId)
    {
        var members = RequireGroup(groupId).MemberIds.ToHashSet();
        return members.Concat(Document.Openings.Where(opening => members.Contains(opening.WallId)).Select(opening => opening.Id)).Distinct().ToList();
    }

    /// <summary>Moves every element of a group by the same translation, as one undoable step. Walls that are not in it stay put.</summary>
    public void MoveElementGroup(Guid groupId, double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) || Math.Abs(deltaX) > 100000 || Math.Abs(deltaY) > 100000)
            throw new InvalidDataException("The move distance is invalid.");
        var members = RequireGroup(groupId).MemberIds.ToHashSet();
        Commit("Transaction.MoveElementGroup", document =>
        {
            var levels = new HashSet<Guid>();
            for (var index = 0; index < document.Walls.Count; index++)
            {
                var wall = document.Walls[index];
                if (!members.Contains(wall.Id)) continue;
                EnsureNotPinned(document, wall.Id);
                levels.Add(wall.LevelId);
                document.Walls[index] = wall with
                {
                    Start = Translate(wall.Start, deltaX, deltaY), End = Translate(wall.End, deltaX, deltaY),
                    GridAxisId = null, GridAxisAssociationDisabled = true
                };
            }
            for (var index = 0; index < document.Floors.Count; index++)
            {
                var floor = document.Floors[index];
                if (!members.Contains(floor.Id)) continue;
                document.Floors[index] = floor with
                {
                    Boundary = floor.Boundary.Select(point => Translate(point, deltaX, deltaY)).ToList(),
                    TopoPoints = floor.TopoPoints?.Select(point => point with { X = point.X + deltaX, Y = point.Y + deltaY }).ToList()
                };
            }
            for (var index = 0; index < document.Columns.Count; index++)
                if (members.Contains(document.Columns[index].Id))
                    document.Columns[index] = document.Columns[index] with { Position = Translate(document.Columns[index].Position, deltaX, deltaY) };
            for (var index = 0; index < document.StructuralElements.Count; index++)
            {
                var element = document.StructuralElements[index];
                if (members.Contains(element.Id))
                    document.StructuralElements[index] = element with { Start = Translate(element.Start, deltaX, deltaY), End = Translate(element.End, deltaX, deltaY) };
            }
            foreach (var level in levels) RefreshRoomBoundariesIfNeeded(document, level);
        });
    }

    /// <summary>
    /// Makes a copy of a group, moved by the given translation and placed in a new group of the same kind. The copy of a
    /// wall keeps its doors and windows; the copy of a curved wall is a curved wall of its own.
    /// </summary>
    public Guid CopyElementGroup(Guid groupId, double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) || Math.Abs(deltaX) > 100000 || Math.Abs(deltaY) > 100000)
            throw new InvalidDataException("The move distance is invalid.");
        var source = RequireGroup(groupId);
        var newGroup = Guid.NewGuid();
        var name = UniqueGroupName(source.Name, source.Kind);
        Commit("Transaction.CopyElementGroup", document =>
        {
            var map = new Dictionary<Guid, Guid>();
            var members = source.MemberIds.ToHashSet();
            var curves = new Dictionary<Guid, Guid>();
            foreach (var wall in document.Walls.Where(item => members.Contains(item.Id)).ToArray())
            {
                map[wall.Id] = Guid.NewGuid();
                document.Walls.Add(wall with
                {
                    Id = map[wall.Id], Start = Translate(wall.Start, deltaX, deltaY), End = Translate(wall.End, deltaX, deltaY),
                    GridAxisId = null, GridAxisAssociationDisabled = true, Pinned = false, CurveGroupId = CurveGroups.Remap(wall, curves).CurveGroupId
                });
            }
            foreach (var opening in document.Openings.Where(item => map.ContainsKey(item.WallId)).ToArray())
            {
                map[opening.Id] = Guid.NewGuid();
                document.Openings.Add(opening with { Id = map[opening.Id], WallId = map[opening.WallId] });
            }
            foreach (var floor in document.Floors.Where(item => members.Contains(item.Id)).ToArray())
            {
                map[floor.Id] = Guid.NewGuid();
                document.Floors.Add(floor with
                {
                    Id = map[floor.Id], Boundary = floor.Boundary.Select(point => Translate(point, deltaX, deltaY)).ToList(),
                    TopoPoints = floor.TopoPoints?.Select(point => point with { X = point.X + deltaX, Y = point.Y + deltaY }).ToList()
                });
            }
            foreach (var column in document.Columns.Where(item => members.Contains(item.Id)).ToArray())
            {
                map[column.Id] = Guid.NewGuid();
                document.Columns.Add(column with { Id = map[column.Id], Position = Translate(column.Position, deltaX, deltaY) });
            }
            foreach (var element in document.StructuralElements.Where(item => members.Contains(item.Id)).ToArray())
            {
                map[element.Id] = Guid.NewGuid();
                document.StructuralElements.Add(element with
                {
                    Id = map[element.Id], Start = Translate(element.Start, deltaX, deltaY), End = Translate(element.End, deltaX, deltaY),
                    GroupId = null, RailingHostId = element.RailingHostId is { } host && map.ContainsKey(host) ? map[host] : null
                });
            }
            var copied = source.MemberIds.Where(map.ContainsKey).Select(id => map[id]).ToList();
            if (copied.Count == 0) throw new InvalidDataException("The group has nothing left to copy.");
            document.ElementGroups.Add(new ElementGroup(newGroup, name, source.Kind, copied));
        });
        return newGroup;
    }

    /// <summary>Deletes every element of a group, with the doors and windows of its walls, as one undoable step.</summary>
    public int RemoveElementGroupMembers(Guid groupId) => RemoveElements(GroupElements(groupId));

    /// <summary>A group as text, to save to a file and load into any project.</summary>
    public string WriteElementGroup(Guid groupId)
    {
        var group = RequireGroup(groupId);
        var members = group.MemberIds.ToHashSet();
        var walls = Document.Walls.Where(item => members.Contains(item.Id)).ToList();
        var wallIds = walls.Select(item => item.Id).ToHashSet();
        var openings = Document.Openings.Where(item => wallIds.Contains(item.WallId)).ToList();
        var data = new ElementGroupFileData(GroupFileFormat, group.Name, group.Kind, walls, openings,
            Document.Floors.Where(item => members.Contains(item.Id)).ToList(), Document.Columns.Where(item => members.Contains(item.Id)).ToList(),
            Document.StructuralElements.Where(item => members.Contains(item.Id)).ToList(),
            Document.WallTypes.Where(type => walls.Any(wall => wall.WallTypeId == type.Id)).ToList(),
            Document.OpeningTypes.Where(type => openings.Any(opening => opening.OpeningTypeId == type.Id)).ToList());
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Loads a group saved by <see cref="WriteElementGroup"/> into the project: every element gets a new identifier, goes on
    /// the given level and is moved by the translation. The wall and opening types it needs are added when the project
    /// lacks them. Returns the new group.
    /// </summary>
    public Guid LoadElementGroup(string text, Guid levelId, double deltaX, double deltaY)
    {
        ElementGroupFileData data;
        try { data = JsonSerializer.Deserialize<ElementGroupFileData>(text ?? string.Empty) ?? throw new InvalidDataException("The file is not a saved group."); }
        catch (JsonException) { throw new InvalidDataException("The file is not a saved group."); }
        if (data.Format != GroupFileFormat || data.Walls is null || data.Openings is null || data.Floors is null || data.Columns is null ||
            data.StructuralElements is null || data.WallTypes is null || data.OpeningTypes is null || !Enum.IsDefined(data.Kind))
            throw new InvalidDataException("The file is not a saved group.");
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) || Math.Abs(deltaX) > 100000 || Math.Abs(deltaY) > 100000)
            throw new InvalidDataException("The move distance is invalid.");
        var newGroup = Guid.NewGuid();
        var name = UniqueGroupName(CleanGroupName(data.Name), data.Kind);
        Commit("Transaction.LoadElementGroup", document =>
        {
            foreach (var type in data.WallTypes.Where(type => document.WallTypes.All(existing => existing.Id != type.Id))) document.WallTypes.Add(type);
            foreach (var type in data.OpeningTypes.Where(type => document.OpeningTypes.All(existing => existing.Id != type.Id))) document.OpeningTypes.Add(type);
            var map = new Dictionary<Guid, Guid>();
            var curves = new Dictionary<Guid, Guid>();
            foreach (var wall in data.Walls)
            {
                map[wall.Id] = Guid.NewGuid();
                document.Walls.Add(wall with
                {
                    Id = map[wall.Id], LevelId = levelId, Start = Translate(wall.Start, deltaX, deltaY), End = Translate(wall.End, deltaX, deltaY),
                    GridAxisId = null, GridAxisAssociationDisabled = true, Pinned = false, TopLevelId = null, TopOffset = 0,
                    CurveGroupId = CurveGroups.Remap(wall, curves).CurveGroupId
                });
            }
            foreach (var opening in data.Openings.Where(item => map.ContainsKey(item.WallId)))
            {
                map[opening.Id] = Guid.NewGuid();
                document.Openings.Add(opening with { Id = map[opening.Id], WallId = map[opening.WallId] });
            }
            foreach (var floor in data.Floors)
            {
                map[floor.Id] = Guid.NewGuid();
                document.Floors.Add(floor with
                {
                    Id = map[floor.Id], LevelId = levelId, Boundary = floor.Boundary.Select(point => Translate(point, deltaX, deltaY)).ToList(),
                    TopoPoints = floor.TopoPoints?.Select(point => point with { X = point.X + deltaX, Y = point.Y + deltaY }).ToList()
                });
            }
            foreach (var column in data.Columns)
            {
                map[column.Id] = Guid.NewGuid();
                document.Columns.Add(column with { Id = map[column.Id], LevelId = levelId, Position = Translate(column.Position, deltaX, deltaY) });
            }
            foreach (var element in data.StructuralElements)
            {
                map[element.Id] = Guid.NewGuid();
                document.StructuralElements.Add(element with
                {
                    Id = map[element.Id], LevelId = levelId, Start = Translate(element.Start, deltaX, deltaY), End = Translate(element.End, deltaX, deltaY),
                    GroupId = null, RailingHostId = element.RailingHostId is { } host && map.ContainsKey(host) ? map[host] : null
                });
            }
            var members = data.Walls.Select(item => item.Id).Concat(data.Floors.Select(item => item.Id)).Concat(data.Columns.Select(item => item.Id))
                .Concat(data.StructuralElements.Select(item => item.Id)).Where(map.ContainsKey).Select(id => map[id]).ToList();
            if (members.Count == 0) throw new InvalidDataException("The file is not a saved group.");
            document.ElementGroups.Add(new ElementGroup(newGroup, name, data.Kind, members));
            foreach (var wallLevel in document.Walls.Where(wall => map.Values.Contains(wall.Id)).Select(wall => wall.LevelId).Distinct().ToArray())
                RefreshRoomBoundariesIfNeeded(document, wallLevel);
        });
        return newGroup;
    }
}
