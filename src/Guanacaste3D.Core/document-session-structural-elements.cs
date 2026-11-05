namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>Adds one structural, foundation, connection, or drafting element using the kind's default section.</summary>
    public Guid AddStructuralElement(Guid levelId, StructuralElementKind kind, string name, Point2 start, Point2 end)
    {
        var id = Guid.NewGuid();
        Commit("Transaction.AddStructuralElement", document =>
        {
            document.StructuralElements.Add(CreateStructuralElement(id, document, levelId, kind, name, start, end));
            if (kind == StructuralElementKind.RoomSeparator &&
                document.Rooms.Any(room => room.IsAutomatic && room.LevelId == levelId))
                RebuildAutomaticRooms(document, levelId);
        });
        return id;
    }

    /// <summary>
    /// Fills a rectangle with evenly spaced beams. Beams run along the longer side, and the first and last
    /// beams sit on the rectangle edges so the layout never leaves a bay wider than <paramref name="spacing"/>.
    /// </summary>
    public int AddBeamSystem(Guid levelId, string name, Point2 first, Point2 opposite, double spacing)
    {
        var minimumX = Math.Min(first.X, opposite.X);
        var maximumX = Math.Max(first.X, opposite.X);
        var minimumY = Math.Min(first.Y, opposite.Y);
        var maximumY = Math.Max(first.Y, opposite.Y);
        var alongX = maximumX - minimumX >= maximumY - minimumY;
        var run = alongX ? maximumX - minimumX : maximumY - minimumY;
        var across = alongX ? maximumY - minimumY : maximumX - minimumX;
        if (run < 1e-6 || across < 1e-6 || spacing <= 0) return 0;

        var divisions = Math.Max(1, (int)Math.Ceiling(across / spacing));
        Commit("Transaction.AddBeamSystem", document =>
        {
            for (var step = 0; step <= divisions; step++)
            {
                var offset = across * step / divisions;
                var start = alongX
                    ? new Point2(minimumX, minimumY + offset)
                    : new Point2(minimumX + offset, minimumY);
                var end = alongX
                    ? new Point2(maximumX, minimumY + offset)
                    : new Point2(minimumX + offset, maximumY);
                document.StructuralElements.Add(CreateStructuralElement(
                    Guid.NewGuid(), document, levelId, StructuralElementKind.Beam, name, start, end));
            }
        });
        return divisions + 1;
    }

    /// <summary>
    /// Places a connection at every joint on a level: a member end that meets another member end or a
    /// column. Joints that already have a connection, or that duplicate one another, are skipped.
    /// </summary>
    public int AddConnectionsAtJoints(Guid levelId, string name, double tolerance = 0.05)
    {
        var members = Document.StructuralElements
            .Where(element => element.LevelId == levelId && element.Length > tolerance &&
                element.Kind is StructuralElementKind.Beam or StructuralElementKind.Brace or StructuralElementKind.Truss)
            .ToArray();
        var ends = members.SelectMany(member => new[] { member.Start, member.End }).ToArray();
        var columns = Document.Columns.Where(column => column.LevelId == levelId)
            .Select(column => column.Position).ToArray();
        var existing = Document.StructuralElements
            .Where(element => element.LevelId == levelId && element.Kind == StructuralElementKind.Connection)
            .Select(element => element.Start).ToArray();

        var joints = new List<Point2>();
        for (var index = 0; index < ends.Length; index++)
        {
            var point = ends[index];
            var meetsMember = ends.Where((_, otherIndex) => otherIndex != index)
                .Any(other => other.DistanceTo(point) <= tolerance);
            var meetsColumn = columns.Any(column => column.DistanceTo(point) <= tolerance);
            if (!meetsMember && !meetsColumn) continue;
            if (joints.Any(joint => joint.DistanceTo(point) <= tolerance)) continue;
            if (existing.Any(connection => connection.DistanceTo(point) <= tolerance)) continue;
            joints.Add(point);
        }
        if (joints.Count == 0) return 0;

        Commit("Transaction.AddConnections", document =>
        {
            foreach (var joint in joints)
                document.StructuralElements.Add(CreateStructuralElement(
                    Guid.NewGuid(), document, levelId, StructuralElementKind.Connection, name, joint, joint));
        });
        return joints.Count;
    }

    /// <summary>
    /// Adds an opening or shaft only when its rectangle (or square, for a single click) lies fully inside a floor
    /// it cuts. Openings cut the floors of their own level; shafts cut every floor at or above their level.
    /// Returns null, and leaves the project untouched, when no such floor contains it.
    /// </summary>
    public Guid? AddOpening(Guid levelId, StructuralElementKind kind, string name, Point2 start, Point2 end)
    {
        if (kind is not (StructuralElementKind.Opening or StructuralElementKind.Shaft))
            throw new ArgumentOutOfRangeException(nameof(kind), "Only openings and shafts cut floor slabs.");
        var levelElevation = Document.Levels.Single(level => level.Id == levelId).Elevation;
        var candidate = new StructuralElement(Guid.NewGuid(), levelId, kind, name, start, end,
            StructuralElementStandards.For(kind).Width, 0, 0);
        var extent = Geometry.PlanRectangle.Of(candidate.Footprint);
        var cutsFloor = Document.Floors.Any(floor =>
            (kind == StructuralElementKind.Shaft
                ? Document.Levels.Single(level => level.Id == floor.LevelId).Elevation >= levelElevation
                : floor.LevelId == levelId && floor.Kind == FloorKind.Floor) &&
            Geometry.SlabOpenings.ContainsRectangle(floor.Boundary, extent));
        if (!cutsFloor) return null;

        var id = candidate.Id;
        Commit("Transaction.AddOpening", document =>
            document.StructuralElements.Add(CreateStructuralElement(id, document, levelId, kind, name, start, end)));
        return id;
    }

    /// <summary>Adds a text annotation at a plan position; the text is stored as the element name.</summary>
    public Guid AddModelText(Guid levelId, Point2 position, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Model text cannot be blank.", nameof(text));
        var id = Guid.NewGuid();
        Commit("Transaction.AddModelText", document => document.StructuralElements.Add(new StructuralElement(
            id, levelId, StructuralElementKind.ModelText, text.Trim(), position, position, 0, 0, 0)));
        return id;
    }

    /// <summary>
    /// Groups the elements on a level whose ends both lie inside the rectangle between two corners. Elements that
    /// already belong to a group are left alone. Returns the group size, or 0 when fewer than two elements qualify.
    /// </summary>
    public int AddStructuralGroup(Guid levelId, Point2 first, Point2 opposite)
    {
        var rectangle = Geometry.PlanRectangle.Of([first, opposite]);
        var members = Document.StructuralElements
            .Where(element => element.LevelId == levelId && element.GroupId is null &&
                rectangle.Contains(element.Start) && rectangle.Contains(element.End))
            .Select(element => element.Id)
            .ToHashSet();
        if (members.Count < 2) return 0;

        var groupId = Guid.NewGuid();
        Commit("Transaction.AddStructuralGroup", document =>
        {
            for (var index = 0; index < document.StructuralElements.Count; index++)
            {
                var existing = document.StructuralElements[index];
                if (members.Contains(existing.Id))
                    document.StructuralElements[index] = existing with { GroupId = groupId };
            }
        });
        return members.Count;
    }

    /// <summary>Removes a structural element from the project, together with its whole group.</summary>
    public void RemoveStructuralElement(Guid id)
    {
        var element = Document.StructuralElements.FirstOrDefault(candidate => candidate.Id == id);
        if (element is null) return;
        var groupId = element.GroupId;
        Commit("Transaction.RemoveStructuralElement", document =>
        {
            var removed = document.StructuralElements.Where(candidate => candidate.Id == id ||
                (groupId is Guid group && candidate.GroupId == group)).Select(candidate => candidate.Id).ToHashSet();
            var affectedRoomLevels = document.StructuralElements
                .Where(candidate => removed.Contains(candidate.Id) &&
                    candidate.Kind == StructuralElementKind.RoomSeparator)
                .Select(candidate => candidate.LevelId).Distinct().ToArray();
            document.StructuralElements.RemoveAll(candidate => removed.Contains(candidate.Id) ||
                (candidate.RailingHostId is Guid host && removed.Contains(host)));
            foreach (var level in affectedRoomLevels)
                if (document.Rooms.Any(room => room.IsAutomatic && room.LevelId == level))
                    RebuildAutomaticRooms(document, level);
        });
    }

    /// <summary>Translates a structural element by a metric offset without changing its section. Group members move together.</summary>
    public void MoveStructuralElement(Guid id, double deltaX, double deltaY)
    {
        var element = Document.StructuralElements.FirstOrDefault(candidate => candidate.Id == id);
        if (element is null) return;
        var groupId = element.GroupId;
        Commit("Transaction.MoveStructuralElement", document =>
        {
            var moved = document.StructuralElements.Where(candidate => candidate.Id == id ||
                (groupId is Guid group && candidate.GroupId == group)).Select(candidate => candidate.Id).ToHashSet();
            for (var index = 0; index < document.StructuralElements.Count; index++)
            {
                var existing = document.StructuralElements[index];
                if (!moved.Contains(existing.Id) &&
                    !(existing.RailingHostId is Guid host && moved.Contains(host))) continue;
                document.StructuralElements[index] = existing with
                {
                    Start = new Point2(existing.Start.X + deltaX, existing.Start.Y + deltaY),
                    End = new Point2(existing.End.X + deltaX, existing.End.Y + deltaY)
                };
                var translated = document.StructuralElements[index];
                if (translated.Kind is StructuralElementKind.Opening or StructuralElementKind.Shaft &&
                    Geometry.SlabOpeningHosts.Resolve(document, translated).Count == 0)
                    throw new InvalidDataException("A floor opening must remain inside a host slab.");
            }
            var affectedRoomLevels = document.StructuralElements
                .Where(candidate => moved.Contains(candidate.Id) &&
                    candidate.Kind == StructuralElementKind.RoomSeparator)
                .Select(candidate => candidate.LevelId).Distinct();
            foreach (var level in affectedRoomLevels)
                if (document.Rooms.Any(room => room.IsAutomatic && room.LevelId == level))
                    RebuildAutomaticRooms(document, level);
        });
    }

    /// <summary>
    /// Adds a dormer whose footprint lies fully inside a roof slab of the same level. Its underside starts on the
    /// roof's top face, and the roof is cut by the same rectangle. Returns null when no roof contains it.
    /// </summary>
    public Guid? AddDormer(Guid levelId, string name, Point2 first, Point2 opposite)
    {
        var extent = Geometry.PlanRectangle.Of([first, opposite]);
        var roof = Document.Floors.FirstOrDefault(floor => floor.LevelId == levelId && floor.Kind == FloorKind.Roof &&
            Geometry.SlabOpenings.ContainsRectangle(floor.Boundary, extent));
        if (roof is null) return null;

        var id = Guid.NewGuid();
        Commit("Transaction.AddDormer", document => document.StructuralElements.Add(
            CreateStructuralElement(id, document, levelId, StructuralElementKind.Dormer, name, first, opposite)
                with { Offset = roof.ElevationOffset + RoofGeometry.LowestRiseOver(roof, Geometry.SlabOpenings.Corners(extent)) }));
        return id;
    }

    /// <summary>Builds an element whose section and offset come from the shared standards for its kind.</summary>
    private static StructuralElement CreateStructuralElement(Guid id, BuildingDocument document, Guid levelId,
        StructuralElementKind kind, string name, Point2 start, Point2 end)
    {
        var standard = StructuralElementStandards.For(kind);
        var number = document.StructuralElements.Count(element => element.Kind == kind) + 1;
        return new StructuralElement(id, levelId, kind, $"{name} {number}", start, end,
            standard.Width, standard.Height, standard.Offset);
    }
}
