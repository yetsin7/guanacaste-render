using Guanacaste3D.Core;

namespace Guanacaste3D.Agent;

/// <summary>Tools that edit the model. Each is one atomic, undoable session transaction.</summary>
internal static class ModelingTools
{
    public static IEnumerable<AgentTool> Create()
    {
        yield return new("add_level",
            "Adds a level above the current highest one, optionally naming it and setting its elevation in meters.",
            Schema.Object(("name", Schema.Str("Level name."), false), ("elevation", Schema.Num("Elevation in meters."), false)),
            ToolRisk.Modify, (context, args) =>
            {
                var id = context.Session.AddLevel();
                var level = context.Session.Document.Levels.Single(l => l.Id == id);
                var name = args.Raw["name"] is null ? level.Name : args.Text("name");
                var elevation = args.Number("elevation", level.Elevation);
                if (name != level.Name || Math.Abs(elevation - level.Elevation) > 1e-9)
                    context.Session.UpdateLevel(id, name, elevation);
                return ReadTools.Json(new { id, name, elevation });
            });

        yield return new("add_wall", "Adds one straight wall between two plan points, in meters.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("x1", Schema.Num("Start X."), true), ("y1", Schema.Num("Start Y."), true),
                ("x2", Schema.Num("End X."), true), ("y2", Schema.Num("End Y."), true),
                ("height", Schema.Num("Wall height. Default 3."), false),
                ("wall_type_id", Schema.Str("Wall type id from get_project_summary. Default is the basic 200 mm wall."), false)),
            ToolRisk.Modify, (context, args) =>
            {
                var typeId = args.Raw["wall_type_id"] is null ? (Guid?)null : args.Id("wall_type_id");
                var id = context.Session.AddWall(args.Level(), new Point2(args.Number("x1"), args.Number("y1")),
                    new Point2(args.Number("x2"), args.Number("y2")), height: args.Number("height", 3), wallTypeId: typeId);
                return ReadTools.Json(new { id });
            });

        yield return new("add_rectangular_walls",
            "Adds four walls forming a closed rectangle. The corner (x, y) is the lower-left corner.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("x", Schema.Num("Lower-left X."), true), ("y", Schema.Num("Lower-left Y."), true),
                ("width", Schema.Num("Size along X."), true), ("depth", Schema.Num("Size along Y."), true),
                ("height", Schema.Num("Wall height. Default 3."), false),
                ("wall_type_id", Schema.Str("Wall type id. Optional."), false)),
            ToolRisk.Modify, (context, args) =>
            {
                double x = args.Number("x"), y = args.Number("y"), w = args.Number("width"), d = args.Number("depth");
                if (w <= 0 || d <= 0) throw new InvalidDataException("width and depth must be positive.");
                var typeId = args.Raw["wall_type_id"] is null ? BuiltInWallTypes.Basic200Id : args.Id("wall_type_id");
                var count = context.Session.AddWallsAroundBoundary(args.Level(),
                    [new(x, y), new(x + w, y), new(x + w, y + d), new(x, y + d)], typeId, args.Number("height", 3));
                return $"Added {count} walls.";
            });

        yield return new("add_opening", "Adds a door or window hosted in a wall. Offsets and sizes are meters along the wall.",
            Schema.Object(
                ("wall_id", Schema.Str("Host wall id."), true),
                ("kind", Schema.Str("Opening kind.", "door", "window"), true),
                ("offset", Schema.Num("Distance from the wall start to the opening center. Default is the wall center."), false),
                ("width", Schema.Num("Opening width."), false),
                ("sill", Schema.Num("Sill height above the floor."), false),
                ("height", Schema.Num("Opening height."), false)),
            ToolRisk.Modify, (context, args) =>
            {
                var kind = args.Text("kind").Equals("window", StringComparison.OrdinalIgnoreCase) ? OpeningKind.Window : OpeningKind.Door;
                var id = context.Session.AddOpening(args.Id("wall_id"), kind, args.OptionalNumber("offset"),
                    args.OptionalNumber("width"), args.OptionalNumber("sill"), args.OptionalNumber("height"));
                return ReadTools.Json(new { id });
            });

        yield return new("add_room", "Adds a rectangular room annotation between two opposite corners.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("x1", Schema.Num("First corner X."), true), ("y1", Schema.Num("First corner Y."), true),
                ("x2", Schema.Num("Opposite corner X."), true), ("y2", Schema.Num("Opposite corner Y."), true),
                ("name", Schema.Str("Room name, for example Kitchen."), false)),
            ToolRisk.Modify, (context, args) =>
            {
                var id = context.Session.AddRoom(args.Level(), new Point2(args.Number("x1"), args.Number("y1")),
                    new Point2(args.Number("x2"), args.Number("y2")), args.Text("name", string.Empty) is { Length: > 0 } n ? n : null);
                return ReadTools.Json(new { id });
            });

        yield return new("add_automatic_rooms", "Adds a room to every closed rectangular space bounded by walls on a level.",
            Schema.Object(("level", Schema.Str("Level id or name."), true)), ToolRisk.Modify,
            (context, args) => $"Added {context.Session.AddAutomaticRooms(args.Level())} rooms.");

        yield return new("add_slab", "Adds a rectangular floor or roof slab. The corner (x, y) is the lower-left corner.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("x", Schema.Num("Lower-left X."), true), ("y", Schema.Num("Lower-left Y."), true),
                ("width", Schema.Num("Size along X."), true), ("depth", Schema.Num("Size along Y."), true),
                ("kind", Schema.Str("Slab kind. Default floor.", "floor", "roof"), false),
                ("thickness", Schema.Num("Thickness in meters. Default 0.20."), false),
                ("name", Schema.Str("Slab name."), false)),
            ToolRisk.Modify, (context, args) =>
            {
                double x = args.Number("x"), y = args.Number("y"), w = args.Number("width"), d = args.Number("depth");
                if (w <= 0 || d <= 0) throw new InvalidDataException("width and depth must be positive.");
                var kind = args.Text("kind", "floor").Equals("roof", StringComparison.OrdinalIgnoreCase) ? FloorKind.Roof : FloorKind.Floor;
                var name = args.Text("name", string.Empty);
                var id = context.Session.AddFloor(args.Level(), [new(x, y), new(x + w, y), new(x + w, y + d), new(x, y + d)],
                    args.Number("thickness", 0.20), name.Length > 0 ? name : null, kind);
                return ReadTools.Json(new { id });
            });

        yield return new("add_column", "Adds a structural column at a plan position.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("x", Schema.Num("X."), true), ("y", Schema.Num("Y."), true),
                ("width", Schema.Num("Default 0.40."), false), ("depth", Schema.Num("Default 0.40."), false),
                ("height", Schema.Num("Default 3."), false)),
            ToolRisk.Modify, (context, args) =>
            {
                var id = context.Session.AddColumn(args.Level(), new Point2(args.Number("x"), args.Number("y")),
                    args.Number("width", 0.40), args.Number("depth", 0.40), args.Number("height", 3));
                return ReadTools.Json(new { id });
            });

        yield return new("add_curtain_wall",
            "Adds a glazed curtain wall (glass with mullions) along a line, 3 m high. Use it for glass facades and pavilions.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("x1", Schema.Num("Start X."), true), ("y1", Schema.Num("Start Y."), true),
                ("x2", Schema.Num("End X."), true), ("y2", Schema.Num("End Y."), true)),
            ToolRisk.Modify, (context, args) =>
            {
                var preset = ArchitecturalLibrary.PresetsFor(StructuralElementKind.CurtainWall).First();
                var id = context.Session.AddElementFromPreset(args.Level(), preset,
                    new Point2(args.Number("x1"), args.Number("y1")), new Point2(args.Number("x2"), args.Number("y2")));
                return ReadTools.Json(new { id });
            });

        yield return new("add_pool",
            "Adds a rectangular reflecting pool whose water surface sits 15 cm below the level, so surrounding paving frames it.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("x", Schema.Num("Lower-left X."), true), ("y", Schema.Num("Lower-left Y."), true),
                ("width", Schema.Num("Size along X."), true), ("depth", Schema.Num("Size along Y."), true)),
            ToolRisk.Modify, (context, args) =>
            {
                double x = args.Number("x"), y = args.Number("y"), w = args.Number("width"), d = args.Number("depth");
                if (w <= 0 || d <= 0) throw new InvalidDataException("width and depth must be positive.");
                var preset = ArchitecturalLibrary.SlabPresets.Single(slab => slab.Kind == FloorKind.Water);
                var id = context.Session.AddSlabFromPreset(args.Level(), [new(x, y), new(x + w, y), new(x + w, y + d), new(x, y + d)],
                    preset, -0.15);
                return ReadTools.Json(new { id });
            });

        yield return new("add_planting", "Plants a tree, palm, cypress or shrub at a plan position.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("x", Schema.Num("X."), true), ("y", Schema.Num("Y."), true),
                ("species", Schema.Str("Plant type.", "broadleaf", "guanacaste", "palm", "cypress", "shrub"), true)),
            ToolRisk.Modify, (context, args) =>
            {
                var name = args.Text("species") switch
                {
                    "broadleaf" => "Broadleaf Tree 8 m",
                    "guanacaste" => "Guanacaste Tree 15 m",
                    "palm" => "Coconut Palm 9 m",
                    "cypress" => "Cypress 12 m",
                    "shrub" => "Shrub 1.5 m",
                    _ => throw new InvalidDataException("Unknown species.")
                };
                var point = new Point2(args.Number("x"), args.Number("y"));
                var id = context.Session.AddElementFromPreset(args.Level(),
                    ArchitecturalLibrary.ElementPresets.Single(preset => preset.Name == name), point, point);
                return ReadTools.Json(new { id });
            });

        yield return new("add_callout",
            "Marks a named detail region (callout) on a level's plan so the plan can zoom to it. Corners are in meters.",
            Schema.Object(
                ("level", Schema.Str("Level id or name."), true),
                ("x1", Schema.Num("First corner X."), true), ("y1", Schema.Num("First corner Y."), true),
                ("x2", Schema.Num("Opposite corner X."), true), ("y2", Schema.Num("Opposite corner Y."), true),
                ("name", Schema.Str("Detail name, for example Cocina."), false)),
            ToolRisk.Modify, (context, args) =>
            {
                var id = context.Session.AddCallout(args.Level(), args.Text("name", string.Empty),
                    new PlanBounds(args.Number("x1"), args.Number("y1"), args.Number("x2"), args.Number("y2")));
                return ReadTools.Json(new { id });
            });

        yield return new("auto_dimension",
            "Adds associative dimensions to every wall length and opening width of a level, as one undoable step. Existing dimensions are kept.",
            Schema.Object(("level", Schema.Str("Level id or name."), true)), ToolRisk.Modify,
            (context, args) => $"Added {context.Session.AutoDimensionLevel(args.Level())} dimensions.");

        yield return new("remove_element", "Deletes one element by id. It can be undone with the undo tool.",
            Schema.Object(
                ("kind", Schema.Str("Element kind.", "wall", "opening", "room", "floor", "column", "level"), true),
                ("id", Schema.Str("Element id."), true)),
            ToolRisk.Modify, (context, args) =>
            {
                var id = args.Id("id");
                var session = context.Session;
                switch (args.Text("kind"))
                {
                    case "wall": session.RemoveWall(id); break;
                    case "opening": session.RemoveOpening(id); break;
                    case "room": session.RemoveRoom(id); break;
                    case "floor": session.RemoveFloor(id); break;
                    case "column": session.RemoveColumn(id); break;
                    case "level": session.RemoveLevel(id); break;
                    default: throw new InvalidDataException("Unknown element kind.");
                }
                return "Removed.";
            });

        yield return new("set_unit_price", "Sets or changes one unit price used by the budget. Codes: WALL_AREA, FLOOR_AREA, " +
            "ROOF_AREA, DOOR, WINDOW, COLUMN.",
            Schema.Object(
                ("code", Schema.Str("Price code."), true), ("price", Schema.Num("Price per unit, in the list currency."), true),
                ("description", Schema.Str("Description."), false), ("unit", Schema.Str("Unit label."), false)),
            ToolRisk.Modify, (context, args) =>
            {
                var code = args.Text("code").ToUpperInvariant();
                var existing = context.Prices.Find(code);
                var price = (decimal)args.Number("price");
                context.Prices = context.Prices.With(new UnitPrice(code,
                    args.Text("description", existing?.Description ?? code), args.Text("unit", existing?.Unit ?? "u"), price));
                return $"{code} = {price} {context.Prices.Currency}.";
            });

        yield return new("undo", "Reverts the most recent model change.", Schema.Object(), ToolRisk.Modify, (context, _) =>
        {
            if (!context.Session.CanUndo) return "Nothing to undo.";
            context.Session.Undo();
            return "Undone.";
        });
    }
}
