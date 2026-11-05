namespace Guanacaste3D.Core;

/// <summary>Validated document editing and undo/redo history, independent of the UI.</summary>
public sealed partial class DocumentSession
{
    public BuildingDocument Document { get; private set; }

    /// <summary>Starts an isolated edit session from a validated document snapshot.</summary>
    public DocumentSession(BuildingDocument document)
    {
        document.Validate();
        Document = document.Clone();
    }

    /// <summary>Adds a straight wall instance using a valid project wall type.</summary>
    public Guid AddWall(Guid levelId, Point2 start, Point2 end, double thickness = 0.20,
        double height = 3, Guid? wallTypeId = null,
        WallLocationReference locationReference = WallLocationReference.Centerline)
    {
        if (!Enum.IsDefined(locationReference))
            throw new InvalidDataException("A wall is invalid or has no associated level.");
        var selectedTypeId = wallTypeId ?? BuiltInWallTypes.Basic200Id;
        if (!Document.WallTypes.Any(type => type.Id == selectedTypeId))
            throw new InvalidDataException("The selected wall type does not exist.");
        if (wallTypeId is not null)
            thickness = Document.WallTypes.Single(type => type.Id == selectedTypeId).Thickness;
        var id = Guid.NewGuid();
        Commit("Transaction.AddWall", doc =>
        {
            var axisId = FindAlignedGridAxis(doc, start, end);
            doc.Walls.Add(new Wall(id, levelId, start, end, thickness, height)
            {
                WallTypeId = selectedTypeId,
                GridAxisId = axisId,
                // A wall tied to an axis keeps its centerline on it, whatever face it was drawn by.
                LocationReference = axisId is null ? locationReference : WallLocationReference.Centerline
            });
            RefreshRoomBoundariesIfNeeded(doc, levelId);
        });
        return id;
    }

    /// <summary>Adds a level three meters above the current highest level.</summary>
    public Guid AddLevel()
    {
        var id = Guid.NewGuid();
        Commit("Transaction.AddLevel", doc =>
        {
            var elevation = doc.Levels.Max(l => l.Elevation) + 3;
            var number = doc.Levels.Count + 1;
            while (doc.Levels.Any(existing => existing.Name == DefaultLabels.Get("LevelName", number))) number++;
            var level = new Level(id, DefaultLabels.Get("LevelName", number), elevation);
            doc.Levels.Add(level);
            doc.Views.Add(BuiltInProjectViews.CreatePlan(level));
        });
        return id;
    }

    /// <summary>
    /// Updates instance dimensions for an existing wall. For a top-constrained
    /// wall the requested height is kept by adjusting its top offset, so the
    /// wall stays tied to its level instead of silently losing the constraint.
    /// </summary>
    public void UpdateWall(Guid id, double thickness, double height)
    {
        if (!Document.Walls.Any(w => w.Id == id)) return;
        Commit("Transaction.UpdateWall", doc =>
        {
            var index = doc.Walls.FindIndex(w => w.Id == id);
            var wall = doc.Walls[index];
            var levels = doc.Levels.ToDictionary(level => level.Id);
            var offset = wall.TopLevelId is Guid topId
                ? levels[wall.LevelId].Elevation + height - levels[topId].Elevation
                : 0;
            doc.Walls[index] = wall with { Height = height, TopOffset = offset };
            WallReferenceLine.Resize(doc, new Dictionary<Guid, double> { [id] = thickness });
            RefreshRoomBoundariesIfNeeded(doc, wall.LevelId);
        });
    }

    /// <summary>Assigns a reusable type and its thickness to an existing wall.</summary>
    public void SetWallType(Guid wallId, Guid wallTypeId)
    {
        var type = Document.WallTypes.FirstOrDefault(candidate => candidate.Id == wallTypeId)
            ?? throw new InvalidDataException("The selected wall type does not exist.");
        if (!Document.Walls.Any(wall => wall.Id == wallId))
            throw new InvalidDataException("The selected wall does not exist.");
        Commit("Transaction.SetWallType", document =>
        {
            var index = document.Walls.FindIndex(wall => wall.Id == wallId);
            document.Walls[index] = document.Walls[index] with { WallTypeId = wallTypeId };
            WallReferenceLine.Resize(document, new Dictionary<Guid, double> { [wallId] = type.Thickness });
            RefreshRoomBoundariesIfNeeded(document, document.Walls[index].LevelId);
        });
    }

    /// <summary>Adds a reusable wall type to the project catalog.</summary>
    public Guid AddWallType(string name, WallFunction function, double thickness, string materialName)
    {
        var id = Guid.NewGuid();
        Commit("Transaction.AddWallType", document => document.WallTypes.Add(
            new WallType(id, name.Trim(), function, thickness, materialName.Trim())));
        return id;
    }

    /// <summary>Updates a wall type and propagates its thickness to every instance.</summary>
    public void UpdateWallType(Guid id, string name, WallFunction function, double thickness, string materialName)
    {
        var existing = Document.WallTypes.FirstOrDefault(type => type.Id == id)
            ?? throw new InvalidDataException("The wall type does not exist.");
        UpdateWallType(id, name, function, thickness, materialName,
            existing.PlanFillColor, existing.PlanFillPattern,
            existing.SectionFillColor, existing.SectionFillPattern);
    }

    /// <summary>Updates type geometry and its coordinated plan and section representation.</summary>
    public void UpdateWallType(
        Guid id,
        string name,
        WallFunction function,
        double thickness,
        string materialName,
        string planFillColor,
        WallFillPattern planFillPattern,
        string sectionFillColor,
        WallFillPattern sectionFillPattern,
        WallConstruction? construction = null,
        double? leftRepello = null,
        double? leftFine = null,
        double? rightRepello = null,
        double? rightFine = null,
        double? studSpacing = null)
    {
        if (!Document.WallTypes.Any(type => type.Id == id))
            throw new InvalidDataException("The wall type does not exist.");
        Commit("Transaction.UpdateWallType", document =>
        {
            var typeIndex = document.WallTypes.FindIndex(type => type.Id == id);
            var previous = document.WallTypes[typeIndex];
            document.WallTypes[typeIndex] = previous with
            {
                Name = name.Trim(), Function = function, Thickness = thickness,
                MaterialName = materialName.Trim(),
                PlanFillColor = planFillColor.ToUpperInvariant(), PlanFillPattern = planFillPattern,
                SectionFillColor = sectionFillColor.ToUpperInvariant(), SectionFillPattern = sectionFillPattern,
                Construction = construction ?? previous.Construction,
                LeftRepelloThickness = leftRepello ?? previous.LeftRepelloThickness,
                LeftFineThickness = leftFine ?? previous.LeftFineThickness,
                RightRepelloThickness = rightRepello ?? previous.RightRepelloThickness,
                RightFineThickness = rightFine ?? previous.RightFineThickness,
                GypsumStudSpacing = studSpacing ?? previous.GypsumStudSpacing
            };
            var affectedLevels = document.Walls.Where(wall => wall.WallTypeId == id)
                .Select(wall => wall.LevelId).Distinct().ToArray();
            // The type thickness reaches every instance, each keeping the face it was placed by.
            WallReferenceLine.Resize(document, document.Walls.Where(wall => wall.WallTypeId == id)
                .ToDictionary(wall => wall.Id, _ => thickness));
            foreach (var levelId in affectedLevels) RefreshRoomBoundariesIfNeeded(document, levelId);
        });
    }

    /// <summary>Sets the plot used by the location plan; null returns to the automatic proposal.</summary>
    public void SetLot(SiteLot? lot)
    {
        if (lot is { IsValid: false }) throw new ArgumentException("The plot dimensions are invalid.", nameof(lot));
        Commit("Transaction.SetLot", document => document.Lot = lot);
    }

    /// <summary>Adds a rectangular room annotation to a level.</summary>
    public Guid AddRoom(Guid levelId, Point2 firstCorner, Point2 secondCorner, string? name = null)
    {
        var id = Guid.NewGuid();
        Commit("Transaction.AddRoom", document =>
        {
            var number = (document.Rooms.Count + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            document.Rooms.Add(new Room(id, levelId, number, name ?? $"Room {number}", firstCorner, secondCorner));
        });
        return id;
    }

    /// <summary>Adds a floor slab using a caller-supplied closed plan boundary.</summary>
    public Guid AddFloor(Guid levelId, IReadOnlyCollection<Point2> boundary, double thickness = 0.20,
        string? name = null, FloorKind kind = FloorKind.Floor, double elevationOffset = 0)
    {
        var id = Guid.NewGuid();
        Commit("Transaction.AddFloor", document => document.Floors.Add(new FloorSlab(
            id, levelId, name ?? $"Floor {document.Floors.Count + 1}", [.. boundary], thickness)
        {
            Kind = kind,
            ElevationOffset = elevationOffset
        }));
        return id;
    }

    /// <summary>Adds a structural column standing on a level at a plan position.</summary>
    public Guid AddColumn(Guid levelId, Point2 position, double width = 0.40, double depth = 0.40,
        double height = 3.00)
    {
        var id = Guid.NewGuid();
        Commit("Transaction.AddColumn", document =>
            document.Columns.Add(new Column(id, levelId, position, width, depth, height)));
        return id;
    }

    /// <summary>Removes a structural column from the project.</summary>
    public void RemoveColumn(Guid id)
    {
        if (Document.Columns.Any(column => column.Id == id))
            Commit("Transaction.RemoveColumn", document => document.Columns.RemoveAll(column => column.Id == id));
    }

    /// <summary>Adds a numbered drawing sheet to the project document set.</summary>
    public Guid AddSheet(SheetPaperSize paperSize = SheetPaperSize.A1, string? name = null)
    {
        var id = Guid.NewGuid();
        Commit("Transaction.AddSheet", document =>
        {
            var index = document.Sheets.Count + 1;
            while (document.Sheets.Any(sheet => sheet.Number == $"A{index:D3}")) index++;
            var family = SheetFamilies.Create(paperSize);
            document.SheetFamilies.Add(family);
            var layout = family.SheetLayout!;
            var plan = document.Views.First(view => view.Kind == ProjectViewKind.Plan);
            var inset = layout.Margin + 8;
            document.Sheets.Add(new DrawingSheet(id, $"A{index:D3}", name ?? DefaultLabels.Get("SheetName", index), paperSize)
            {
                TitleBlockFamilyId = family.Id,
                Viewports = [new(Guid.NewGuid(), plan.Id, inset, inset + layout.HeaderHeight,
                    layout.Width - 2 * inset, layout.Height - 2 * inset - layout.HeaderHeight - layout.FooterHeight)]
            });
        });
        return id;
    }

    /// <summary>Removes a room annotation from the project.</summary>
    public void RemoveRoom(Guid id)
    {
        if (Document.Rooms.Any(room => room.Id == id))
            Commit("Transaction.RemoveRoom", document => document.Rooms.RemoveAll(room => room.Id == id));
    }

    /// <summary>Removes a floor slab from the project.</summary>
    public void RemoveFloor(Guid id)
    {
        if (Document.Floors.Any(floor => floor.Id == id))
            Commit("Transaction.RemoveFloor", document => document.Floors.RemoveAll(floor => floor.Id == id));
    }

    /// <summary>Removes a drawing sheet from the project.</summary>
    public void RemoveSheet(Guid id)
    {
        if (Document.Sheets.Any(sheet => sheet.Id == id))
            Commit("Transaction.RemoveSheet", document => document.Sheets.RemoveAll(sheet => sheet.Id == id));
    }

    /// <summary>
    /// Moves a wall and its hosted openings without changing dimensions. Walls meeting it at a
    /// corner keep that corner, so their coincident endpoints travel with it.
    /// </summary>
    public void MoveWall(Guid id, double deltaX, double deltaY)
    {
        if (!Document.Walls.Any(w => w.Id == id)) return;
        Commit("Transaction.MoveWall", doc =>
        {
            EnsureNotPinned(doc, id);
            var index = doc.Walls.FindIndex(w => w.Id == id);
            var wall = doc.Walls[index];
            // The pieces of a curved wall travel together; the walls that met it follow its ends.
            var group = wall.CurveGroupId is { } curve
                ? doc.Walls.Select((w, position) => (w, position)).Where(entry => entry.w.CurveGroupId == curve).Select(entry => entry.position).ToArray()
                : [index];
            var movedCorners = group.SelectMany(position => new[] { doc.Walls[position].Start, doc.Walls[position].End }).ToArray();
            foreach (var position in group)
            {
                var piece = doc.Walls[position];
                EnsureNotPinned(doc, piece.Id);
                doc.Walls[position] = piece with
                {
                    Start = Translate(piece.Start, deltaX, deltaY),
                    End = Translate(piece.End, deltaX, deltaY),
                    GridAxisId = null, // Manual wall motion releases its reference-datum constraint.
                    GridAxisAssociationDisabled = true
                };
            }
            for (var other = 0; other < doc.Walls.Count; other++)
            {
                var neighbour = doc.Walls[other];
                if (group.Contains(other) || neighbour.LevelId != wall.LevelId) continue;
                var nextStart = FollowCorner(neighbour.Start, movedCorners, deltaX, deltaY);
                var nextEnd = FollowCorner(neighbour.End, movedCorners, deltaX, deltaY);
                if (nextStart != neighbour.Start || nextEnd != neighbour.End)
                    doc.Walls[other] = neighbour with { Start = nextStart, End = nextEnd, GridAxisId = null };
            }
            RefreshRoomBoundariesIfNeeded(doc, wall.LevelId);
        });
    }

    /// <summary>Moves a room annotation by a metric translation.</summary>
    public void MoveRoom(Guid id, double deltaX, double deltaY)
    {
        if (!Document.Rooms.Any(room => room.Id == id)) return;
        Commit("Transaction.MoveRoom", document =>
        {
            var index = document.Rooms.FindIndex(room => room.Id == id);
            var existing = document.Rooms[index];
            document.Rooms[index] = existing with
            {
                FirstCorner = Translate(existing.FirstCorner, deltaX, deltaY),
                SecondCorner = Translate(existing.SecondCorner, deltaX, deltaY)
            };
        });
    }

    /// <summary>Moves a floor slab's boundary by a metric translation.</summary>
    public void MoveFloor(Guid id, double deltaX, double deltaY)
    {
        if (!Document.Floors.Any(floor => floor.Id == id)) return;
        Commit("Transaction.MoveFloor", document =>
        {
            var index = document.Floors.FindIndex(floor => floor.Id == id);
            var existing = document.Floors[index];
            document.Floors[index] = existing with
            {
                Boundary = existing.Boundary.Select(point => Translate(point, deltaX, deltaY)).ToList(),
                TopoPoints = existing.TopoPoints?.Select(point =>
                    point with { X = point.X + deltaX, Y = point.Y + deltaY }).ToList()
            };
        });
    }

    /// <summary>Moves a structural column by a metric translation in plan.</summary>
    public void MoveColumn(Guid id, double deltaX, double deltaY)
    {
        if (!Document.Columns.Any(column => column.Id == id)) return;
        Commit("Transaction.MoveColumn", document =>
        {
            var index = document.Columns.FindIndex(column => column.Id == id);
            var existing = document.Columns[index];
            document.Columns[index] = existing with { Position = Translate(existing.Position, deltaX, deltaY) };
        });
    }

    /// <summary>Moves one end of a straight wall to a new plan point, keeping its length direction tied to that end.</summary>
    public void MoveWallEnd(Guid id, WallEnd end, Point2 point)
    {
        if (!Document.Walls.Any(w => w.Id == id))
            throw new InvalidDataException("The wall does not exist.");
        Commit("Transaction.MoveWallEnd", doc =>
        {
            EnsureNotPinned(doc, id);
            var index = doc.Walls.FindIndex(w => w.Id == id);
            var wall = doc.Walls[index];
            doc.Walls[index] = end == WallEnd.Start ? wall with { Start = point, GridAxisId = null, GridAxisAssociationDisabled = true } : wall with { End = point, GridAxisId = null, GridAxisAssociationDisabled = true };
            RefreshRoomBoundariesIfNeeded(doc, wall.LevelId);
        });
    }

    /// <summary>Translates a point by a metric offset.</summary>
    private static Point2 Translate(Point2 point, double deltaX, double deltaY) =>
        new(point.X + deltaX, point.Y + deltaY);

    /// <summary>Translates a neighbouring wall's endpoint only when it coincides with a moved corner.</summary>
    private static Point2 FollowCorner(Point2 point, Point2[] movedCorners, double deltaX, double deltaY) =>
        movedCorners.Any(corner => corner.DistanceTo(point) <= WallJoinIndex.Tolerance)
            ? Translate(point, deltaX, deltaY)
            : point;

    /// <summary>Duplicates a wall and all of its openings with new stable identifiers.</summary>
    public Guid DuplicateWall(Guid id, double deltaX = 1, double deltaY = 1)
    {
        var original = Document.Walls.FirstOrDefault(w => w.Id == id)
            ?? throw new InvalidDataException("The wall to duplicate does not exist.");
        var newId = Guid.NewGuid();
        Commit("Transaction.DuplicateWall", doc =>
        {
            doc.Walls.Add(original with
            {
                Id = newId,
                Start = new Point2(original.Start.X + deltaX, original.Start.Y + deltaY),
                End = new Point2(original.End.X + deltaX, original.End.Y + deltaY),
                // A copied wall must never inherit a reference constraint from its source.
                GridAxisId = null,
                GridAxisAssociationDisabled = true,
                Pinned = false,
                CurveGroupId = null,
                CurveRadius = 0
            });
            foreach (var opening in doc.Openings.Where(o => o.WallId == id).ToArray())
                doc.Openings.Add(opening with { Id = Guid.NewGuid(), WallId = newId });
            RefreshRoomBoundariesIfNeeded(doc, original.LevelId);
        });
        return newId;
    }

    /// <summary>Rotates a straight wall around its starting point, retaining hosted openings.</summary>
    public void RotateWall(Guid id, double degrees)
    {
        if (!double.IsFinite(degrees))
            throw new InvalidDataException("The rotation angle is invalid.");
        if (!Document.Walls.Any(w => w.Id == id))
            throw new InvalidDataException("The wall does not exist.");
        if (degrees % 360 == 0) return;
        var radians = degrees * Math.PI / 180;
        Commit("Transaction.RotateWall", doc =>
        {
            EnsureNotPinned(doc, id);
            var index = doc.Walls.FindIndex(w => w.Id == id);
            var wall = doc.Walls[index];
            var dx = wall.End.X - wall.Start.X;
            var dy = wall.End.Y - wall.Start.Y;
            doc.Walls[index] = wall with
            {
                End = new Point2(
                    wall.Start.X + dx * Math.Cos(radians) - dy * Math.Sin(radians),
                    wall.Start.Y + dx * Math.Sin(radians) + dy * Math.Cos(radians)),
                GridAxisId = null,
                GridAxisAssociationDisabled = true
            };
            RefreshRoomBoundariesIfNeeded(doc, wall.LevelId);
        });
    }

    /// <summary>Removes a hosted opening when it exists.</summary>
    public void RemoveOpening(Guid id)
    {
        if (!Document.Openings.Any(o => o.Id == id)) return;
        Commit("Transaction.RemoveOpening", doc => doc.Openings.RemoveAll(o => o.Id == id));
    }

    /// <summary>Removes a wall and cascades removal to its hosted openings.</summary>
    public void RemoveWall(Guid id)
    {
        if (!Document.Walls.Any(w => w.Id == id)) return;
        Commit("Transaction.RemoveWall", doc =>
        {
            var removed = doc.Walls.Single(w => w.Id == id);
            // A curved wall is one wall: deleting any piece of it deletes the chain.
            var members = removed.CurveGroupId is { } curve
                ? doc.Walls.Where(w => w.CurveGroupId == curve).Select(w => w.Id).ToHashSet()
                : [id];
            foreach (var member in members) EnsureNotPinned(doc, member);
            var levelId = removed.LevelId;
            doc.Openings.RemoveAll(o => members.Contains(o.WallId));
            doc.Walls.RemoveAll(w => members.Contains(w.Id));
            RefreshRoomBoundariesIfNeeded(doc, levelId);
        });
    }

    /// <summary>Renames the active project after trimming surrounding whitespace.</summary>
    public void Rename(string name) => Commit("Transaction.Rename", document => document.Name = name.Trim());
}
