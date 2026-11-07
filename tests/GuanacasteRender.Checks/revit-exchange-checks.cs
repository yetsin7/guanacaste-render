using System.Numerics;
using System.Runtime.InteropServices;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Import.Revit;

/// <summary>Checks that a Revit exchange file becomes native walls, openings, slabs, rooms and shared mesh families.</summary>
static class RevitExchangeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var file = new ExchangeFile
        {
            Format = "guanacaste-exchange", Version = 1, ProjectName = "Casa de prueba",
            Levels = [new() { Id = 1, Name = "Nivel 1", Elevation = 0 }, new() { Id = 2, Name = "Nivel 2", Elevation = 3 }],
            Materials =
            [
                new() { Name = "Ladrillo rojo", OwnerCategory = "OST_Walls" },
                new() { Name = "Tela gris", OwnerCategory = "OST_Furniture" },
                new() { Name = "Vidrio claro", Transparency = 0.8 }
            ],
            Meshes = [Box(new Vector3(-0.4f, -0.3f, 0), new Vector3(0.4f, 0.3f, 0.8f), 1), Box(new Vector3(0, -0.1f, 0), new Vector3(6, 0.1f, 2.7f), 0)]
        };
        // Revit's own frame is far from the origin, as in a georeferenced project.
        const double x0 = 1000, y0 = 2000;
        Wall(file, 10, [x0, y0], [x0 + 6, y0]);
        Wall(file, 11, [x0 + 6, y0], [x0 + 6, y0 + 4]);
        file.Elements.Add(new ExchangeElement
        {
            Id = 20, Category = "OST_Doors", Kind = "door", Family = "Puerta sencilla", Type = "0.90 x 2.10", Level = 1,
            Opening = new ExchangeOpening { Host = 10, Point = [x0 + 2, y0, 0], Width = 0.9, Height = 2.1, Facing = [0, -1], Hand = [1, 0] }
        });
        file.Elements.Add(new ExchangeElement
        {
            Id = 21, Category = "OST_Windows", Kind = "window", Family = "Ventana corrediza", Type = "1.20 x 1.00", Level = 1,
            Opening = new ExchangeOpening { Host = 10, Point = [x0 + 4.2, y0, 0.9], Width = 1.2, Height = 1.0, SillZ = 0.9 }
        });
        file.Elements.Add(new ExchangeElement
        {
            Id = 30, Category = "OST_Floors", Kind = "floor", Type = "Losa 150", Level = 1,
            Slab = new ExchangeSlab
            {
                Flat = true, Z = 0, Thickness = 0.15,
                Loops = [[[x0, y0], [x0 + 6, y0], [x0 + 6, y0 + 4], [x0, y0 + 4]], [[x0 + 1, y0 + 1], [x0 + 2, y0 + 1], [x0 + 2, y0 + 2], [x0 + 1, y0 + 2]]]
            }
        });
        file.Elements.Add(new ExchangeElement
        {
            Id = 31, Category = "OST_Roofs", Kind = "roof", Type = "Techo inclinado", Level = 2, Mesh = 1, Origin = [x0, y0 + 4, 3],
            Slab = new ExchangeSlab { Flat = false }
        });
        foreach (var (id, rotation, x) in new[] { (40L, 0.0, 1.0), (41L, Math.PI / 2, 3.0) })
            file.Elements.Add(new ExchangeElement
            {
                Id = id, Category = "OST_Furniture", Kind = "instance", Family = "Sillón", Type = "Gris", Level = 1, Mesh = 0,
                Origin = [x0 + x, y0 + 2, 0], Rotation = rotation
            });
        file.Elements.Add(new ExchangeElement
        {
            Id = 50, Category = "OST_Rooms", Kind = "room", Name = "Sala", Number = "1", Level = 1,
            Loops = [[[x0 + 0.1, y0 + 0.1], [x0 + 5.9, y0 + 0.1], [x0 + 5.9, y0 + 3.9], [x0 + 0.1, y0 + 3.9]]]
        });

        var (document, report) = ExchangeConverter.Convert(file);
        check(document.Levels.Count == 2 && document.Walls.Count == 2 && report.Walls == 2,
            "Straight Revit walls become native walls on their levels");
        check(document.Walls.All(wall => Math.Abs(wall.Start.X) < 10 && Math.Abs(wall.Start.Y) < 10),
            "A model far from the origin is moved next to it");
        check(document.Openings.Count(opening => opening.Kind == OpeningKind.Door) == 1 &&
              document.Openings.Single(opening => opening.Kind == OpeningKind.Window) is { Sill: 0.9, Width: 1.2 },
            "Hosted doors and windows keep their size and sill");
        check(document.OpeningTypes.Any(type => type.FamilyName == "Ventana corrediza" && type.Operation == OpeningOperation.Sliding),
            "Opening types keep the Revit family name and recognize sliding windows");
        check(document.Floors.Count == 1 && Math.Abs(document.Floors[0].CalculateArea() - 24) < 1e-6 &&
              document.StructuralElements.Count(element => element.Kind == StructuralElementKind.Opening) == 1,
            "A flat floor keeps its outline and its rectangular hole becomes a slab opening");
        check(document.Rooms.Count == 1 && document.Rooms[0].Name == "Sala", "Rectangular rooms are kept");
        var chairs = document.StructuralElements.Where(element => element.Kind == StructuralElementKind.Component && element.Name == "Gris").ToList();
        check(chairs.Count == 2 && chairs.Select(chair => chair.FamilyId).Distinct().Count() == 1 &&
              Math.Abs(chairs[1].Rotation - Math.PI / 2) < 1e-9 && Math.Abs(chairs[0].Width - 0.8) < 1e-5,
            "Repeated furniture shares one mesh family, placed with its own turn");
        check(document.ComponentFamilies.Count == 2 && report.MeshFallbacks.ContainsKey("Sloped roof"),
            "A sloped roof keeps Revit's geometry as its own family");
        var sofa = document.ComponentFamilies.First(family => family.Category == FamilyCategory.Furniture).Mesh!.Decode();
        check(sofa.Kinds.All(kind => kind == SurfaceKind.Upholstery), "Revit materials map onto render surfaces");
        check(document.WallTypes.Any(type => type.Construction == WallConstruction.Brick), "Brick wall layers give a brick wall type");
        document.Validate();
        RealModelCases(check);
    }

    /// <summary>Cases found converting a real house: sunk walls, gables, split slabs and light cut-outs.</summary>
    private static void RealModelCases(Action<bool, string> check)
    {
        var file = new ExchangeFile
        {
            Format = "guanacaste-exchange", Version = 1,
            Levels = [new() { Id = 1, Name = "Sísmica", Elevation = -0.1 }, new() { Id = 2, Name = "Planta baja", Elevation = 0.001 }],
            Materials = [new() { Name = "Brick, Common" }],
            Meshes = [Gable(4f, 0.15f, 2.7f, 3.7f)]
        };
        // A wall on "Planta baja" sunk 10 cm into the ground, attached to the roof: Revit draws its gable.
        file.Elements.Add(new ExchangeElement
        {
            Id = 1, Category = "OST_Walls", Kind = "wall", Type = "Muro Existente", Level = 2, Mesh = 0, Origin = [0, 0, -0.099],
            Wall = new ExchangeWall
            {
                Width = 0.15, Start = [0, 0], End = [4, 0], BaseLevel = 2, BaseZ = -0.099, Height = 3.3, TopZ = 3.601, TopAttached = true,
                Layers = [new ExchangeLayer { Function = "Structure", Width = 0.15, Material = "Brick, Common" }]
            }
        });
        file.Elements.Add(new ExchangeElement
        {
            Id = 2, Category = "OST_Ceilings", Kind = "ceiling", Type = "Cielo", Level = 2,
            Slab = new ExchangeSlab
            {
                Flat = true, Z = 2.6,
                Loops = [[[0, 0], [4, 0], [4, 3], [0, 3]], Circle(2, 1.5, 0.08), [[5, 0], [6, 0], [6, 1], [5, 1]]]
            }
        });
        var (document, report) = ExchangeConverter.Convert(file, "Casa real");
        var ground = document.Levels.Single(level => level.Role == LevelRole.BaseFloor);
        var wall = document.Walls.SingleOrDefault();
        check(ground.Name == "Planta baja" && wall?.LevelId == ground.Id,
            "A wall sunk below its level stays on it, and the level most walls stand on is the ground floor");
        check(wall is not null && Math.Abs(wall.Height - 2.6) < 0.01 &&
              document.ComponentFamilies.Any(family => family.Name.StartsWith("Remate de muro", StringComparison.Ordinal)),
            "A gable wall keeps a native body up to its eaves and Revit's gable above it");
        check(document.Floors.Count(floor => floor.Kind == FloorKind.Ceiling) == 2 && report.MeshFallbacks.Count == 0,
            "Separate pieces of one ceiling become separate slabs, and light cut-outs are ignored");
        document.Validate();
    }

    private static List<double[]> Circle(double x, double y, double radius) =>
        Enumerable.Range(0, 16).Select(index => new[]
        {
            x + radius * Math.Cos(index * Math.PI / 8), y + radius * Math.Sin(index * Math.PI / 8)
        }).ToList();

    /// <summary>A wall along X with a gable: eaves at <paramref name="eaves"/>, ridge at mid-length.</summary>
    private static ExchangeMesh Gable(float length, float width, float eaves, float ridge)
    {
        Vector2[] outline = [new(0, 0), new(length, 0), new(length, eaves), new(length / 2, ridge), new(0, eaves)];
        var positions = new List<Vector3>();
        var indices = new List<int>();
        foreach (var y in new[] { -width / 2, width / 2 })
        {
            var start = positions.Count;
            positions.AddRange(outline.Select(point => new Vector3(point.X, y, point.Y)));
            for (var index = 1; index < outline.Length - 1; index++)
                indices.AddRange(y < 0 ? [start, start + index, start + index + 1] : [start, start + index + 1, start + index]);
        }
        for (var index = 0; index < outline.Length; index++)
        {
            var next = (index + 1) % outline.Length;
            indices.AddRange([index, outline.Length + index, outline.Length + next, index, outline.Length + next, next]);
        }
        var materials = new int[indices.Count / 3];
        return new ExchangeMesh
        {
            Positions = Convert.ToBase64String(MemoryMarshal.AsBytes(positions.ToArray().AsSpan())),
            Indices = Convert.ToBase64String(MemoryMarshal.AsBytes(indices.ToArray().AsSpan())),
            Materials = Convert.ToBase64String(MemoryMarshal.AsBytes(materials.AsSpan()))
        };
    }

    private static void Wall(ExchangeFile file, long id, double[] start, double[] end) =>
        file.Elements.Add(new ExchangeElement
        {
            Id = id, Category = "OST_Walls", Kind = "wall", Type = "Bloque 15", Level = 1,
            Wall = new ExchangeWall
            {
                Width = 0.15, Function = "Exterior", Start = start, End = end, BaseLevel = 1, BaseZ = 0, Height = 2.7, TopZ = 2.7,
                Layers = [new ExchangeLayer { Function = "Structure", Width = 0.15, Material = "Ladrillo rojo" }]
            }
        });

    private static ExchangeMesh Box(Vector3 min, Vector3 max, int material)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = positions.Count;
            positions.AddRange([a, b, c, d]);
            indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }
        var (x0, y0, z0, x1, y1, z1) = (min.X, min.Y, min.Z, max.X, max.Y, max.Z);
        Face(new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0));
        Face(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1));
        Face(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1));
        Face(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0));
        Face(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0));
        Face(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1));
        var materials = Enumerable.Repeat(material, indices.Count / 3).ToArray();
        return new ExchangeMesh
        {
            Positions = Convert.ToBase64String(MemoryMarshal.AsBytes(positions.ToArray().AsSpan())),
            Indices = Convert.ToBase64String(MemoryMarshal.AsBytes(indices.ToArray().AsSpan())),
            Materials = Convert.ToBase64String(MemoryMarshal.AsBytes(materials.AsSpan()))
        };
    }
}
