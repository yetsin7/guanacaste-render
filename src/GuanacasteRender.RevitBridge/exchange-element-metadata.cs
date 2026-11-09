using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace Guanacaste3D.RevitBridge;

/// <summary>Extracts semantic element metadata required to reconstruct supported native BIM elements.</summary>
internal sealed partial class ExchangeExporter
{
    /// <summary>Preserves wall centerlines, constraints, layer materials, and edited-profile flags for native conversion.</summary>
    private JsonObject DescribeWall(Wall wall)
    {
        var description = new JsonObject
        {
            ["wallKind"] = wall.WallType.Kind.ToString(),
            ["width"] = wall.Width * Meters,
            ["function"] = wall.WallType.Function.ToString()
        };
        if (wall.Location is LocationCurve { Curve: { } curve })
        {
            // Revit places the wall on its location line (a face or the core); Guanacaste walls run on the centerline.
            var structure = wall.WallType.GetCompoundStructure();
            var key = (WallLocationLine)(wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM)?.AsInteger() ?? 0);
            var offset = structure?.GetOffsetForLocationLine(key) ?? 0;
            var shift = wall.Orientation.Multiply(-offset);
            var start = curve.GetEndPoint(0) + shift;
            var end = curve.GetEndPoint(1) + shift;
            description["start"] = new JsonArray(start.X * Meters, start.Y * Meters);
            description["end"] = new JsonArray(end.X * Meters, end.Y * Meters);
            description["curved"] = curve is not Line;
            description["locationLine"] = key.ToString();
            description["locationOffset"] = offset * Meters;
            description["orientation"] = new JsonArray(wall.Orientation.X, wall.Orientation.Y);
            if (curve is Arc arc)
            {
                description["center"] = new JsonArray((arc.Center.X + shift.X) * Meters, (arc.Center.Y + shift.Y) * Meters);
                description["radius"] = arc.Radius * Meters;
            }
        }
        var baseLevel = _document.GetElement(wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId()
                                             ?? ElementId.InvalidElementId) as Level;
        var baseOffset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0;
        if (baseLevel is not null)
        {
            description["baseLevel"] = baseLevel.Id.Value;
            description["baseZ"] = (baseLevel.ProjectElevation + baseOffset) * Meters;
        }
        description["height"] = (wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0) * Meters;
        if (wall.get_BoundingBox(null) is { } box)
        {
            description["bottomZ"] = box.Min.Z * Meters;
            description["topZ"] = box.Max.Z * Meters;
        }
        var topLevel = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId();
        if (topLevel is not null && topLevel != ElementId.InvalidElementId) description["topLevel"] = topLevel.Value;
        description["topOffset"] = (wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.AsDouble() ?? 0) * Meters;
        description["profileEdited"] = wall.SketchId != ElementId.InvalidElementId;
        description["topAttached"] = wall.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger() == 1;
        description["baseAttached"] = wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger() == 1;
        var layers = new JsonArray();
        if (wall.WallType.GetCompoundStructure() is { } compound)
            foreach (var layer in compound.GetLayers())
            {
                var material = _document.GetElement(layer.MaterialId) as Material;
                layers.Add(new JsonObject
                {
                    ["function"] = layer.Function.ToString(),
                    ["width"] = layer.Width * Meters,
                    ["material"] = material?.Name ?? "",
                    ["class"] = material?.MaterialClass ?? ""
                });
            }
        description["layers"] = layers;
        return description;
    }

    /// <summary>Preserves opening dimensions, host identity, orientation, and absolute sill elevation.</summary>
    private JsonObject DescribeOpening(FamilyInstance instance, Wall host)
    {
        var point = (instance.Location as LocationPoint)?.Point ?? instance.GetTotalTransform().Origin;
        var door = instance.Category?.Id.Value == (long)BuiltInCategory.OST_Doors;
        var width = Length(instance, door ? BuiltInParameter.DOOR_WIDTH : BuiltInParameter.WINDOW_WIDTH,
            BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM);
        var height = Length(instance, door ? BuiltInParameter.DOOR_HEIGHT : BuiltInParameter.WINDOW_HEIGHT,
            BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM);
        var roughWidth = Length(instance, BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM);
        var roughHeight = Length(instance, BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM);
        var sill = instance.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.AsDouble();
        var levelZ = _document.GetElement(instance.LevelId) is Level level ? level.ProjectElevation : (double?)null;
        var description = new JsonObject
        {
            ["host"] = host.Id.Value,
            ["point"] = new JsonArray(point.X * Meters, point.Y * Meters, point.Z * Meters),
            ["facing"] = new JsonArray(instance.FacingOrientation.X, instance.FacingOrientation.Y),
            ["hand"] = new JsonArray(instance.HandOrientation.X, instance.HandOrientation.Y),
            ["facingFlipped"] = instance.FacingFlipped,
            ["handFlipped"] = instance.HandFlipped,
            ["mirrored"] = instance.Mirrored
        };
        if (width is { } w) description["width"] = w * Meters;
        if (height is { } h) description["height"] = h * Meters;
        if (roughWidth is { } rw) description["roughWidth"] = rw * Meters;
        if (roughHeight is { } rh) description["roughHeight"] = rh * Meters;
        if (sill is { } s && levelZ is { } z) description["sillZ"] = (z + s) * Meters;
        return description;
    }

    /// <summary>The first length found on the instance, then on its type, among the given parameters.</summary>
    private static double? Length(FamilyInstance instance, params BuiltInParameter[] parameters)
    {
        foreach (var parameter in parameters)
        {
            foreach (var owner in new Element[] { instance, instance.Symbol })
                if (owner.get_Parameter(parameter) is { HasValue: true, StorageType: StorageType.Double } value &&
                    value.AsDouble() > 1e-6)
                    return value.AsDouble();
        }
        return null;
    }

    /// <summary>Outline loops, top elevation and thickness of a floor, ceiling or roof; flat when it has one level top.</summary>
    private JsonObject? DescribeSlab(HostObject host)
    {
        var references = host is Ceiling ? HostObjectUtils.GetBottomFaces(host) : HostObjectUtils.GetTopFaces(host);
        var faces = references.Select(reference => host.GetGeometryObjectFromReference(reference)).OfType<Face>().ToList();
        if (faces.Count == 0) return null;
        var flat = faces.Count == 1 && faces[0] is PlanarFace { FaceNormal.Z: var nz } && Math.Abs(nz) > 0.9995;
        var description = new JsonObject { ["flat"] = flat };
        if (faces[0] is PlanarFace planar)
        {
            var loops = new JsonArray();
            foreach (var loop in planar.GetEdgesAsCurveLoops())
            {
                var points = new JsonArray();
                foreach (var curve in loop)
                {
                    var tessellated = curve.Tessellate();
                    for (var index = 0; index < tessellated.Count - 1; index++)
                        points.Add(new JsonArray(tessellated[index].X * Meters, tessellated[index].Y * Meters));
                }
                loops.Add(points);
            }
            description["loops"] = loops;
            description["z"] = planar.Origin.Z * Meters;
        }
        var thickness = host switch
        {
            Floor => host.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble(),
            RoofBase => host.get_Parameter(BuiltInParameter.ROOF_ATTR_THICKNESS_PARAM)?.AsDouble(),
            _ => host.get_Parameter(BuiltInParameter.CEILING_THICKNESS)?.AsDouble()
        };
        if (thickness is > 0) description["thickness"] = thickness.Value * Meters;
        if (host is RoofBase) description["roofClass"] = host.GetType().Name;
        return description;
    }

    /// <summary>Exports bounded room loops and areas without creating or modifying Revit elements.</summary>
    private void VisitRoom(SpatialElement room)
    {
        if (room.Area <= 0) return;
        var loops = new JsonArray();
        foreach (var segments in room.GetBoundarySegments(new SpatialElementBoundaryOptions()))
        {
            var points = new JsonArray();
            foreach (var segment in segments)
            {
                var tessellated = segment.GetCurve().Tessellate();
                for (var index = 0; index < tessellated.Count - 1; index++)
                    points.Add(new JsonArray(tessellated[index].X * Meters, tessellated[index].Y * Meters));
            }
            loops.Add(points);
        }
        _elements.Add(new JsonObject
        {
            ["id"] = room.Id.Value,
            ["uniqueId"] = room.UniqueId,
            ["category"] = nameof(BuiltInCategory.OST_Rooms),
            ["categoryName"] = room.Category?.Name ?? "",
            ["kind"] = "room",
            ["name"] = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name,
            ["number"] = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
            ["level"] = room.LevelId.Value,
            ["area"] = room.Area * Meters * Meters,
            ["loops"] = loops
        });
    }

    /// <summary>The level an element belongs to: its own, its host's, or its schedule level.</summary>
    private static ElementId? LevelOf(Element element)
    {
        if (element.LevelId != ElementId.InvalidElementId) return element.LevelId;
        foreach (var parameter in new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                     BuiltInParameter.WALL_BASE_CONSTRAINT, BuiltInParameter.STAIRS_BASE_LEVEL_PARAM })
            if (element.get_Parameter(parameter)?.AsElementId() is { } id && id != ElementId.InvalidElementId) return id;
        return element is FamilyInstance { Host: { } host } ? LevelOf(host) : null;
    }

    /// <summary>Aggregates skipped-element reasons for the exchange conversion report.</summary>
    private void Count(string key) => _skipped[key] = _skipped.GetValueOrDefault(key) + 1;
}
