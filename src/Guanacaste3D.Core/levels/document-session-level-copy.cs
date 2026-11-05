using System.Globalization;

namespace Guanacaste3D.Core;

/// <summary>
/// Copying a whole storey onto other levels, as Revit's "Paste aligned to selected levels": the typical floor of a
/// multi-storey building is modelled once and repeated.
/// </summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Copies everything that belongs to one storey onto each target level in one undoable step: walls with their doors
    /// and windows, rooms, slabs, columns, framing, stairs, components and fixtures, and the electrical installation
    /// with its panel circuits and switch links. Elements that already span several storeys (shafts and elevators) are
    /// not copied, and a wall tied to the level above stays tied to the level above its new level. Room numbers are
    /// renumbered for the storey they land on. With a <paramref name="selection"/>, only the selected elements of the
    /// storey are copied (with the openings of selected walls and the circuits of selected panels), as Revit copies a
    /// selection to the clipboard and pastes it aligned. Returns the number of elements created.
    /// </summary>
    public int CopyLevelContent(Guid sourceLevelId, IReadOnlyCollection<Guid> targetLevelIds, IReadOnlySet<Guid>? selection = null)
    {
        var ordered = Document.Levels.OrderBy(level => level.Elevation).ToList();
        var sourceIndex = ordered.FindIndex(level => level.Id == sourceLevelId);
        if (sourceIndex < 0) throw new InvalidDataException("The source level does not exist.");
        var targets = targetLevelIds.Distinct().Where(id => id != sourceLevelId).ToArray();
        if (targets.Any(id => ordered.All(level => level.Id != id)))
            throw new InvalidDataException("A target level does not exist.");
        if (targets.Length == 0) return 0;

        var created = 0;
        Commit("Transaction.CopyLevelContent", document =>
        {
            foreach (var target in targets)
                created += CopyStorey(document, ordered, sourceIndex, ordered.FindIndex(level => level.Id == target), selection);
        });
        return created;
    }

    /// <summary>Copies one storey onto one level; returns the number of elements created.</summary>
    private static int CopyStorey(BuildingDocument document, List<Level> ordered, int sourceIndex, int targetIndex,
        IReadOnlySet<Guid>? selection)
    {
        bool Selected(Guid id) => selection is null || selection.Contains(id);
        var source = ordered[sourceIndex].Id;
        var target = ordered[targetIndex].Id;
        var ids = new Dictionary<Guid, Guid>();
        Guid Map(Guid id) => ids.TryGetValue(id, out var mapped) ? mapped : ids[id] = Guid.NewGuid();
        Guid? MapLevel(Guid? level)
        {
            if (level is not Guid id) return null;
            var index = ordered.FindIndex(candidate => candidate.Id == id) - sourceIndex + targetIndex;
            return index >= 0 && index < ordered.Count ? ordered[index].Id : null;
        }
        var count = 0;

        foreach (var wall in document.Walls.Where(wall => wall.LevelId == source && Selected(wall.Id)).ToArray())
        {
            var topLevel = MapLevel(wall.TopLevelId);
            document.Walls.Add(wall with
            {
                Id = Map(wall.Id), LevelId = target, TopLevelId = topLevel,
                TopOffset = topLevel is null ? 0 : wall.TopOffset
            });
            count++;
        }
        foreach (var opening in document.Openings.Where(opening => ids.ContainsKey(opening.WallId)).ToArray())
        {
            document.Openings.Add(opening with { Id = Map(opening.Id), WallId = ids[opening.WallId] });
            count++;
        }
        var numbers = document.Rooms.Select(room => room.Number).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var room in document.Rooms.Where(room => room.LevelId == source && Selected(room.Id)).ToArray())
        {
            var number = RoomNumberFor(room.Number, sourceIndex, targetIndex);
            for (var suffix = 2; numbers.Contains(number); suffix++)
                number = $"{RoomNumberFor(room.Number, sourceIndex, targetIndex)}-{suffix}";
            numbers.Add(number);
            document.Rooms.Add(room with { Id = Map(room.Id), LevelId = target, Number = number });
            count++;
        }
        foreach (var floor in document.Floors.Where(floor => floor.LevelId == source && Selected(floor.Id)).ToArray())
        {
            document.Floors.Add(floor with { Id = Map(floor.Id), LevelId = target, Boundary = [.. floor.Boundary] });
            count++;
        }
        foreach (var column in document.Columns.Where(column => column.LevelId == source && Selected(column.Id)).ToArray())
        {
            document.Columns.Add(column with { Id = Map(column.Id), LevelId = target });
            count++;
        }
        var copiedElements = document.StructuralElements.Where(element => element.LevelId == source && Selected(element.Id) &&
                     element.Kind is not (StructuralElementKind.Shaft or StructuralElementKind.Elevator)).ToArray();
        foreach (var element in copiedElements) Map(element.Id);
        foreach (var element in copiedElements)
        {
            document.StructuralElements.Add(element with
            {
                Id = Map(element.Id), LevelId = target, GroupId = element.GroupId is Guid group ? Map(group) : null,
                RailingHostId = element.RailingHostId is Guid host && ids.TryGetValue(host, out var copiedHost)
                    ? copiedHost : null
            });
            count++;
        }

        // Electrical: a panel copied with its storey brings its circuits; devices on a circuit of a panel that stays
        // behind keep that circuit, so a shared riser circuit still feeds the copies.
        var devices = document.ElectricalDevices.Where(device => device.LevelId == source && Selected(device.Id)).ToArray();
        foreach (var device in devices) Map(device.Id);
        foreach (var circuit in document.ElectricalCircuits.Where(circuit => ids.ContainsKey(circuit.PanelId)).ToArray())
            document.ElectricalCircuits.Add(circuit with { Id = Map(circuit.Id), PanelId = ids[circuit.PanelId] });
        foreach (var device in devices)
        {
            document.ElectricalDevices.Add(device with
            {
                Id = ids[device.Id], LevelId = target,
                CircuitId = device.CircuitId is Guid circuit && ids.TryGetValue(circuit, out var copied) ? copied : device.CircuitId,
                // A switch keeps controlling the copies of its own storey's lights; links to other storeys are dropped.
                Controls = device.Controls.Where(ids.ContainsKey).Select(id => ids[id]).ToList()
            });
            count++;
        }
        // A fixture copied with its storey joins every plumbing system the original belongs to.
        for (var index = 0; index < document.PlumbingSystems.Count; index++)
        {
            var system = document.PlumbingSystems[index];
            var added = system.Fixtures.Where(ids.ContainsKey).Select(id => ids[id]).ToList();
            if (added.Count > 0) document.PlumbingSystems[index] = system with { Fixtures = [.. system.Fixtures, .. added] };
        }
        return count;
    }

    /// <summary>
    /// The number a copied room takes on its new storey. Rooms numbered by storey (101, 102 on the first storey; 201
    /// on the second) take the target storey's prefix; any other number gets the target storey as a suffix.
    /// </summary>
    public static string RoomNumberFor(string number, int sourceIndex, int targetIndex)
    {
        var sourcePrefix = (sourceIndex + 1).ToString(CultureInfo.InvariantCulture);
        var targetPrefix = (targetIndex + 1).ToString(CultureInfo.InvariantCulture);
        if (number.Length > sourcePrefix.Length && number.StartsWith(sourcePrefix, StringComparison.Ordinal) &&
            number.Length - sourcePrefix.Length == 2 && number[sourcePrefix.Length..].All(char.IsAsciiDigit))
            return targetPrefix + number[sourcePrefix.Length..];
        return $"{number}.{targetPrefix}";
    }
}
