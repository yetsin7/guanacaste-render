using System.Numerics;
using Guanacaste3D.Core.Rendering;

/// <summary>Renders the demonstration house and the daylit room at presentation size, for judging the look of the renderer.</summary>
static class ShowcaseShot
{
    /// <summary>Renders only the requested scenes, avoiding the memory cost of unrelated detailed models.</summary>
    public static void Run(string folder, int width, int height, int passes)
    {
        Directory.CreateDirectory(folder);
        /// <summary>Frames the house without the extended ground plane affecting the camera position.</summary>
        BenchmarkScene EyeLevel()
        {
            var house = BenchmarkScenes.HouseExterior();
            var (min, max) = Guanacaste3D.Core.Geometry.BuildingMesher.Build(Guanacaste3D.Core.SampleProjects.CreateDemonstrationHouse()).Bounds();
            return house with
            {
                Name = "house-eye-level",
                Camera = new RenderCamera(new Vector3(min.X - 6, min.Y - 9, 1.6f), new Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, 1.4f), 55)
                { CorrectVerticals = true }
            };
        }
        /// <summary>Constructs the garden only when it is selected for rendering.</summary>
        BenchmarkScene Garden()
        {
            var (min, max) = Guanacaste3D.Core.Geometry.BuildingMesher.Build(Guanacaste3D.Core.SampleProjects.CreateDemonstrationHouse()).Bounds();
            return GardenScene(min, max);
        }
        var factories = new Dictionary<string, Func<BenchmarkScene>>
        {
            ["house-exterior"] = BenchmarkScenes.HouseExterior,
            ["house-eye-level"] = EyeLevel,
            ["garden"] = Garden,
            ["daylit-room"] = BenchmarkScenes.DaylitRoom,
            ["villa"] = Villa,
            ["cobogo"] = Cobogo,
            ["cobogo-facade-detail"] = () => Cobogo() with { Name = "cobogo-facade-detail", Camera = new RenderCamera(new Vector3(4.6f, -10.5f, 2.3f), new Vector3(4.6f, 0, 2.5f), 48) { CorrectVerticals = true } },
            ["cobogo-garage"] = () => Cobogo() with { Name = "cobogo-garage", Camera = new RenderCamera(new Vector3(9.5f, -9, 1.5f), new Vector3(11.6f, 2.4f, 0.9f), 50) { CorrectVerticals = true } },
            ["night"] = NightInterior
        };
        // G3_SHOT_FAMILIES=<folder> adds "plant-lab": every mesh family of the folder in a row on a lawn, for judging them.
        if (Environment.GetEnvironmentVariable("G3_SHOT_FAMILIES") is { } lab) factories["plant-lab"] = () => PlantLab(lab);
        var only = Environment.GetEnvironmentVariable("G3_SHOT_ONLY");
        foreach (var (name, create) in factories)
        {
            if (only is not null ? name != only : name == "night") continue;
            var scene = create();
            if (float.TryParse(Environment.GetEnvironmentVariable("G3_SHOT_FOV"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fieldOfView) && fieldOfView is >= 5 and <= 150)
                scene = scene with { Camera = scene.Camera with { VerticalFieldOfViewDegrees = fieldOfView } };
            var builder = new RenderSceneBuilder().AddBuilding(scene.Mesh, scene.Materials);
            foreach (var light in scene.Lights) builder.AddLight(light);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var tracer = new PathTracer(builder.Build(), width, height, scene.Camera, scene.Settings);
            for (var i = 0; i < passes; i++) tracer.RenderPass();
            PngCodec.Save(Path.Combine(folder, $"shot-{scene.Name}.png"), RasterImage.FromBgra32(width, height, tracer.ToBgra32(true, new ImageFinish { AutoExposure = true })));
            PngCodec.Save(Path.Combine(folder, $"shot-{scene.Name}-aces.png"), RasterImage.FromBgra32(width, height,
                tracer.ToBgra32(true, new ImageFinish { AutoExposure = true, ToneMapper = ToneMapper.Aces })));
            // G3_SHOT_STYLE=<id>[,<id>...] also saves the picture in those picture styles, for comparing them.
            foreach (var style in (Environment.GetEnvironmentVariable("G3_SHOT_STYLE") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
                PngCodec.Save(Path.Combine(folder, $"shot-{scene.Name}-{style}.png"), RasterImage.FromBgra32(width, height,
                    tracer.ToBgra32(true, RenderLooks.Style(style).Apply(new ImageFinish { AutoExposure = true }))));
            Console.WriteLine($"{scene.Name}: {passes} passes in {watch.Elapsed.TotalSeconds:0.0} s");
        }
    }

    /// <summary>Mesh families of a folder side by side on a lawn under a three-quarter sun, seen at eye level.</summary>
    private static BenchmarkScene PlantLab(string folder)
    {
        var session = new Guanacaste3D.Core.DocumentSession(Guanacaste3D.Core.ArchitecturalLibrary.CreateProject("Plantas"));
        var level = session.Document.Levels[0].Id;
        var lawn = Guanacaste3D.Core.ArchitecturalLibrary.SlabPresets.Single(preset => preset.Name == "Lawn 150 mm");
        session.AddSlabFromPreset(level, [new(-30, -30), new(30, -30), new(30, 30), new(-30, 30)], lawn);
        var x = 0.0;
        foreach (var file in Directory.GetFiles(folder, "*.g3df").Order())
        {
            var family = Guanacaste3D.Core.NativeDocumentStore.LoadFamily(file);
            var id = session.LoadComponentFamily(family);
            var size = family.Mesh!.Decode().Size;
            x += size.X / 2;
            session.PlaceComponentFamily(level, id, new(x, 0), 0.4);
            x += size.X / 2 + 0.4;
        }
        var mesh = Guanacaste3D.Core.Geometry.BuildingMesher.Build(session.Document, includeGround: true,
            plants: Guanacaste3D.Core.Geometry.PlantDetail.Render);
        var (min, max) = mesh.Bounds();
        var height = Math.Max(1.2f, max.Z);
        // G3_SHOT_LAB_ZOOM brings the camera closer (2 is half the distance, aimed lower, at trunks and leaves).
        var zoom = float.TryParse(Environment.GetEnvironmentVariable("G3_SHOT_LAB_ZOOM"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var factor) && factor > 0 ? factor : 1;
        var target = new Vector3((float)x / 2, 0, height * 0.45f / MathF.Sqrt(zoom));
        return new BenchmarkScene("plant-lab", mesh,
            new RenderCamera(target + new Vector3(0, -Math.Max(1.5f, (float)x * 0.9f) / zoom, height * 0.2f / zoom), target, 45) { CorrectVerticals = true },
            new RenderSettings { SunDirection = Vector3.Normalize(new Vector3(-0.5f, -0.6f, 0.65f)), MaxBounces = 5 });
    }

    /// <summary>The modern villa at golden hour, seen from the pool side: the look the photoreal pipeline aims for.</summary>
    private static BenchmarkScene Villa()
    {
        var mesh = Guanacaste3D.Core.Geometry.BuildingMesher.Build(Guanacaste3D.Core.ModernHouseShowcase.Create(), includeGround: true);
        var settings = RenderPresets.Lighting(LightingScenario.Sunny, 10.6, 80, 15.5, RenderQuality.High);
        return new BenchmarkScene("villa", mesh,
            new RenderCamera(new Vector3(-6, -21, 1.7f), new Vector3(9, -2, 3.4f), 58) { CorrectVerticals = true }, settings);
    }

    /// <summary>
    /// The breeze-block house from across the street, the sun low in the west-southwest so the tree's shadow falls across
    /// the white volume; seen frontally with corrected verticals, the way architects photograph a street facade.
    /// </summary>
    private static BenchmarkScene Cobogo()
    {
        // The detailed tree and car families ship with the desktop application; the bench reads them from its sources.
        var folder = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Guanacaste3D.Desktop", "resources", "families");
        var families = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.g3df").Select(Guanacaste3D.Core.NativeDocumentStore.LoadFamily).ToArray()
            : [];
        var mesh = Guanacaste3D.Core.Geometry.BuildingMesher.Build(Guanacaste3D.Core.BreezeBlockHouseShowcase.Create(families),
            includeGround: true, plants: Guanacaste3D.Core.Geometry.PlantDetail.Render);
        // The same camera, sun and filters the desktop application restores when it opens the sample.
        var setup = Guanacaste3D.Core.BreezeBlockHouseShowcase.StreetView;
        var camera = new Guanacaste3D.Core.Navigation.NavigationCamera
        {
            Position = new Vector3((float)setup.CameraX, (float)setup.CameraY, (float)setup.CameraZ),
            YawDegrees = (float)setup.YawDegrees, PitchDegrees = (float)setup.PitchDegrees,
            FieldOfViewDegrees = (float)setup.FieldOfViewDegrees
        };
        // G3_SHOT_SKY=<id> lights the house with one of the panel's skies instead of its saved one.
        if (RenderLooks.Skies.FirstOrDefault(sky => sky.Id == Environment.GetEnvironmentVariable("G3_SHOT_SKY")) is { } sky)
            setup = setup with
            {
                Scenario = sky.Scenario, DayOfYear = sky.DayOfYear, SolarHour = sky.SolarHour, CloudCover = sky.CloudCover,
                Clouds = sky.Clouds, Haze = sky.Haze
            };
        var lighting = RenderPresets.Lighting(setup.Scenario, setup.Latitude, setup.DayOfYear, setup.SolarHour, setup.Quality ?? RenderQuality.High);
        var storm = setup.Clouds == CloudStyle.Storm;
        var settings = lighting with
        {
            AerialPerspective = setup.Haze ? 1f : 0f, CloudCover = (float)setup.CloudCover, Clouds = setup.Clouds,
            SunIntensity = lighting.SunIntensity * (storm ? 0.55f : 1f), SkyIntensity = lighting.SkyIntensity * (storm ? 0.6f : 1f)
        };
        return new BenchmarkScene("cobogo", mesh, camera.ToRenderCamera(20) with { CorrectVerticals = setup.CorrectVerticals }, settings);
    }

    /// <summary>
    /// Rebuilds the demonstration house from the catalog: exposed brick outside, a six-panel front door, louvered and
    /// flush doors inside, and barred windows, for judging the construction materials and leaf styles.
    /// </summary>
    private static void ApplyCatalog(Guanacaste3D.Core.DocumentSession session)
    {
        var document = session.Document;
        document.WallTypes.AddRange(Guanacaste3D.Core.ArchitecturalLibrary.WallTypes().Where(type => document.WallTypes.All(existing => existing.Id != type.Id)));
        document.OpeningTypes.AddRange(Guanacaste3D.Core.ArchitecturalLibrary.OpeningTypes().Where(type => document.OpeningTypes.All(existing => existing.Id != type.Id)));
        var brick = document.WallTypes.Single(type => type.Name == "Exposed Brick 150 mm");
        foreach (var wall in document.Walls.ToArray())
        {
            var current = document.WallTypes.Single(type => type.Id == wall.WallTypeId);
            if (current.Function != Guanacaste3D.Core.WallFunction.Exterior) continue;
            try { session.SetWallType(wall.Id, brick.Id); } catch (InvalidDataException) { }
        }
        var styles = new[] { "Six-Panel Door", "Louvered Door", "Flush Door" };
        var doorIndex = 0;
        foreach (var opening in document.Openings.ToArray())
        {
            var current = document.OpeningTypes.Single(type => type.Id == opening.OpeningTypeId);
            var replacement = opening.Kind == Guanacaste3D.Core.OpeningKind.Door
                ? document.OpeningTypes.Where(type => type.FamilyName == styles[Math.Min(doorIndex++, styles.Length - 1)])
                    .OrderBy(type => Math.Abs(type.Width - current.Width)).FirstOrDefault()
                : document.OpeningTypes.Where(type => type.Kind == Guanacaste3D.Core.OpeningKind.Window && type.SecurityBars)
                    .OrderBy(type => Math.Abs(type.Width - current.Width) + Math.Abs(type.Height - current.Height)).FirstOrDefault();
            if (replacement is null) continue;
            try { session.SetOpeningType(opening.Id, replacement.Id); } catch (InvalidDataException) { }
        }
    }

    /// <summary>
    /// Building services seen from outside: a minisplit on the wall beside the front door (its condenser and line set
    /// outside), and a bathroom and kitchen piped automatically, whose vent stacks rise through the roof.
    /// </summary>
    private static void ApplyServices(Guanacaste3D.Core.DocumentSession session)
    {
        var document = session.Document;
        var level = document.Levels[0].Id;
        var door = document.Openings.First(opening => opening.Kind == Guanacaste3D.Core.OpeningKind.Door);
        var host = document.Walls.Single(wall => wall.Id == door.WallId);
        var (cx, cy) = (document.Walls.Average(wall => (wall.Start.X + wall.End.X) / 2), document.Walls.Average(wall => (wall.Start.Y + wall.End.Y) / 2));
        var (dx, dy) = ((host.End.X - host.Start.X) / host.Length, (host.End.Y - host.Start.Y) / host.Length);
        var (nx, ny) = (-dy, dx);
        var mid = (door.Begin + door.End) / 2;
        var at = new Guanacaste3D.Core.Point2(host.Start.X + dx * mid, host.Start.Y + dy * mid);
        // The inward normal points toward the middle of the house.
        if (nx * (cx - at.X) + ny * (cy - at.Y) < 0) (nx, ny) = (-nx, -ny);
        var along = door.End + 1.0 < host.Length - 0.6 ? door.End + 1.0 : door.Begin - 1.0;
        var inside = new Guanacaste3D.Core.Point2(host.Start.X + dx * along + nx * host.Thickness / 2, host.Start.Y + dy * along + ny * host.Thickness / 2);
        session.AddElectricalDevice(level, Guanacaste3D.Core.ElectricalDeviceKind.MiniSplit, "AC", inside, Math.Atan2(ny, nx));

        Guanacaste3D.Core.ElementPreset Preset(string name) =>
            Guanacaste3D.Core.ResidentialComponentCatalog.Presets.Single(preset => preset.Name == name);
        // A bathroom along the south wall of the first bedroom: each fixture turned half round so its back is on the wall.
        var bedroom = document.Rooms.Single(candidate => candidate.Name == "Dormitorio 1");
        var (x0, south) = (Math.Min(bedroom.FirstCorner.X, bedroom.SecondCorner.X), Math.Min(bedroom.FirstCorner.Y, bedroom.SecondCorner.Y));
        foreach (var (name, x) in new[] { ("Toilet", 0.8), ("Pedestal Sink", 1.8), ("Shower Enclosure", 3.1), ("Water Heater", 4.4) })
        {
            var preset = Preset(name);
            var point = new Guanacaste3D.Core.Point2(x0 + x, south + preset.Depth / 2 + 0.02);
            session.AddElementFromPreset(level, preset, point, point, Math.PI);
        }
        var y0 = south;
        var (minX, minY) = (document.Walls.Min(wall => Math.Min(wall.Start.X, wall.End.X)), document.Walls.Min(wall => Math.Min(wall.Start.Y, wall.End.Y)));
        session.AddPlumbingPoint(level, Guanacaste3D.Core.PlumbingPointKind.WaterMeter, "M", new Guanacaste3D.Core.Point2(minX - 1, minY - 1));
        session.AddPlumbingPoint(level, Guanacaste3D.Core.PlumbingPointKind.SewerOutlet, "S", new Guanacaste3D.Core.Point2(minX - 1, y0 - 0.5));
        session.ConnectPlumbing((kind, number) => $"{kind} {number}");
    }

    /// <summary>The demonstration house at dusk, lit by a ceiling light in every room and a floor lamp, seen from inside.</summary>
    private static BenchmarkScene NightInterior()
    {
        var session = new Guanacaste3D.Core.DocumentSession(Guanacaste3D.Core.SampleProjects.CreateDemonstrationHouse());
        var level = session.Document.Levels[0].Id;
        var lights = Guanacaste3D.Core.ArchitecturalLibrary.ElementPresets.Where(preset => preset.ComponentShape is
            Guanacaste3D.Core.ResidentialComponentShape.CeilingLight or Guanacaste3D.Core.ResidentialComponentShape.FloorLamp).ToArray();
        var ceiling = lights.First(preset => preset.ComponentShape == Guanacaste3D.Core.ResidentialComponentShape.CeilingLight);
        var lamp = lights.First(preset => preset.ComponentShape == Guanacaste3D.Core.ResidentialComponentShape.FloorLamp);
        var rooms = session.Document.Rooms.ToArray();
        foreach (var room in rooms)
        {
            var c = new Guanacaste3D.Core.Point2((room.FirstCorner.X + room.SecondCorner.X) / 2, (room.FirstCorner.Y + room.SecondCorner.Y) / 2);
            session.AddElementFromPreset(level, ceiling, c, c);
        }
        var first = rooms[0];
        var corner = new Guanacaste3D.Core.Point2(Math.Min(first.FirstCorner.X, first.SecondCorner.X) + 0.6, Math.Max(first.FirstCorner.Y, first.SecondCorner.Y) - 0.6);
        session.AddElementFromPreset(level, lamp, corner, corner);
        Guanacaste3D.Core.ElementPreset Of(Guanacaste3D.Core.ResidentialComponentShape shape) =>
            Guanacaste3D.Core.ArchitecturalLibrary.ElementPresets.First(preset => preset.ComponentShape == shape);
        var fx = Math.Min(first.FirstCorner.X, first.SecondCorner.X);
        var fy = Math.Min(first.FirstCorner.Y, first.SecondCorner.Y);
        var gy = Math.Max(first.FirstCorner.Y, first.SecondCorner.Y);
        void Place(Guanacaste3D.Core.ResidentialComponentShape shape, double x, double y)
        {
            var at = new Guanacaste3D.Core.Point2(x, y);
            session.AddElementFromPreset(level, Of(shape), at, at);
        }
        if (Environment.GetEnvironmentVariable("G3_SHOT_ELECTRICAL") == "1")
        {
            var gx = Math.Max(first.FirstCorner.X, first.SecondCorner.X);
            foreach (var (x, y) in new[] { (fx + 1.5, fy + 1.5), (gx - 1.5, fy + 1.5), (fx + 1.5, gy - 1.5), (gx - 1.5, gy - 1.5) })
                session.AddElectricalDevice(level, Guanacaste3D.Core.ElectricalDeviceKind.Downlight, "D", new Guanacaste3D.Core.Point2(x, y), ceiling: 2.7);
            var pendant = session.AddElectricalDevice(level, Guanacaste3D.Core.ElectricalDeviceKind.Pendant, "P", new Guanacaste3D.Core.Point2(fx + 1.6, fy + 1.4), ceiling: 2.7);
            if (double.TryParse(Environment.GetEnvironmentVariable("G3_SHOT_DIM"), System.Globalization.CultureInfo.InvariantCulture, out var dim))
            {
                var dimmer = session.AddElectricalDevice(level, Guanacaste3D.Core.ElectricalDeviceKind.Dimmer, "SD", new Guanacaste3D.Core.Point2(fx + 0.1, fy + 1.0), facing: 0);
                session.ToggleSwitchLink(dimmer, pendant);
                session.SetDimLevel(dimmer, dim);
            }
            session.AddElectricalDevice(level, Guanacaste3D.Core.ElectricalDeviceKind.CeilingFan, "V", new Guanacaste3D.Core.Point2(fx + 2.2, gy - 2.3), ceiling: 2.7);
            var sconce = session.AddElectricalDevice(level, Guanacaste3D.Core.ElectricalDeviceKind.WallSconce, "A", new Guanacaste3D.Core.Point2(fx + 0.1, (fy + gy) / 2), facing: 0);
            var sw = session.AddElectricalDevice(level, Guanacaste3D.Core.ElectricalDeviceKind.Switch, "S", new Guanacaste3D.Core.Point2(fx + 0.1, fy + 0.8), facing: 0);
            session.ToggleSwitchLink(sw, sconce);
            session.ToggleSwitch(sw);
            session.AddElectricalDevice(level, Guanacaste3D.Core.ElectricalDeviceKind.Outlet, "T", new Guanacaste3D.Core.Point2(fx + 0.1, fy + 2.2), facing: 0);
            foreach (var element in session.Document.StructuralElements.Where(element => element.ComponentShape == Guanacaste3D.Core.ResidentialComponentShape.CeilingLight).ToArray())
                session.RemoveStructuralElement(element.Id);
        }
        Place(Guanacaste3D.Core.ResidentialComponentShape.Rug, fx + 2.0, gy - 1.6);
        if (Environment.GetEnvironmentVariable("G3_SHOT_FURNISH") == "1")
        {
            // The newer families: a sectional, a round table with chairs turned to face it (a family's front is -Y, so a
            // chair west of the table turns +90° to face east), an ottoman and a wall mirror.
            Place(Guanacaste3D.Core.ResidentialComponentShape.Sectional, fx + 1.9, gy - 1.05);
            Place(Guanacaste3D.Core.ResidentialComponentShape.Ottoman, fx + 2.4, gy - 2.6);
            Place(Guanacaste3D.Core.ResidentialComponentShape.RoundTable, fx + 1.6, fy + 1.4);
            foreach (var (dx, dy, turn) in new[] { (-0.75, 0.0, Math.PI / 2), (0.75, 0.0, -Math.PI / 2), (0.0, -0.75, Math.PI) })
            {
                var at = new Guanacaste3D.Core.Point2(fx + 1.6 + dx, fy + 1.4 + dy);
                session.AddElementFromPreset(level, Of(Guanacaste3D.Core.ResidentialComponentShape.Chair), at, at, turn);
            }
            var mirror = new Guanacaste3D.Core.Point2(fx + 0.02, (fy + gy) / 2 + 0.6);
            session.AddElementFromPreset(level, Guanacaste3D.Core.ArchitecturalLibrary.ElementPresets.First(preset => preset.Name == "Wall Mirror"),
                mirror, mirror, Math.PI / 2);
        }
        else
        {
            Place(Guanacaste3D.Core.ResidentialComponentShape.Sofa, fx + 2.0, gy - 0.6);
            Place(Guanacaste3D.Core.ResidentialComponentShape.Table, fx + 1.6, fy + 1.4);
            Place(Guanacaste3D.Core.ResidentialComponentShape.Chair, fx + 1.0, fy + 1.4);
            Place(Guanacaste3D.Core.ResidentialComponentShape.Chair, fx + 2.2, fy + 1.4);
        }
        var mesh = Guanacaste3D.Core.Geometry.BuildingMesher.Build(session.Document, includeGround: true);
        var low = new Vector3((float)Math.Min(first.FirstCorner.X, first.SecondCorner.X), (float)Math.Min(first.FirstCorner.Y, first.SecondCorner.Y), 0);
        var high = new Vector3((float)Math.Max(first.FirstCorner.X, first.SecondCorner.X), (float)Math.Max(first.FirstCorner.Y, first.SecondCorner.Y), 0);
        return new BenchmarkScene("night", mesh,
            new RenderCamera(new Vector3(high.X - 0.4f, low.Y + 0.4f, 1.6f), new Vector3(low.X + 0.5f, high.Y - 0.5f, 1.2f), 70) { CorrectVerticals = true },
            Environment.GetEnvironmentVariable("G3_SHOT_DAY") == "1"
                ? new RenderSettings()
                : new RenderSettings { SunIntensity = 0, SkyIntensity = 0.03f, SunDirection = new Vector3(-0.5f, -0.5f, 0.02f) });
    }

    /// <summary>The demonstration house with one tree of every species around it, seen from the garden.</summary>
    private static BenchmarkScene GardenScene(Vector3 min, Vector3 max)
    {
        var session = new Guanacaste3D.Core.DocumentSession(Guanacaste3D.Core.SampleProjects.CreateDemonstrationHouse());
        var level = session.Document.Levels[0].Id;
        if (Environment.GetEnvironmentVariable("G3_SHOT_CATALOG") == "1") ApplyCatalog(session);
        var species = Guanacaste3D.Core.ArchitecturalLibrary.PresetsFor(Guanacaste3D.Core.StructuralElementKind.Planting).ToArray();
        var spots = new (int Species, double X, double Y)[]
        {
            (1, min.X - 13, max.Y + 6), (0, max.X + 4, min.Y - 3), (2, min.X - 3, min.Y - 4), (2, min.X - 6, min.Y - 1),
            (3, max.X + 3, max.Y + 3), (3, max.X + 6, max.Y - 1), (4, min.X + 2, min.Y - 2.5), (4, min.X + 5, min.Y - 2.2),
            (4, max.X - 2, min.Y - 2.4), (0, max.X + 14, max.Y + 12)
        };
        foreach (var (index, x, y) in spots)
            session.AddElementFromPreset(level, species[index], new Guanacaste3D.Core.Point2(x, y), new Guanacaste3D.Core.Point2(x, y));
        if (Environment.GetEnvironmentVariable("G3_SHOT_TERRAIN") == "1")
        {
            var preset = Guanacaste3D.Core.ArchitecturalLibrary.SlabPresets.Single(slab => slab.Kind == Guanacaste3D.Core.FloorKind.Toposolid);
            var site = session.AddSlabFromPreset(level, [new(min.X - 40, min.Y - 30), new(max.X + 40, min.Y - 30),
                new(max.X + 40, max.Y + 45), new(min.X - 40, max.Y + 45)], preset, -0.05);
            foreach (var (x, y, z) in new[] { (min.X - 22.0, max.Y + 14, 4.0), (max.X + 25, max.Y + 30, 9.0), (max.X + 20, min.Y - 18, -2.5),
                         (min.X - 30, min.Y - 20, 1.5), (min.X - 1.0, min.Y - 1, -0.05), (max.X + 1, max.Y + 1, -0.05),
                         (min.X - 1.0, max.Y + 1, -0.05), (max.X + 1, min.Y - 1, -0.05) })
                session.AddTopoPoint(site, new Guanacaste3D.Core.Geometry.TopoPoint(x, y, z));
        }
        if (Environment.GetEnvironmentVariable("G3_SHOT_ROOF") == "1")
        {
            var gable = Guanacaste3D.Core.ArchitecturalLibrary.SlabPresets.Single(slab => slab.Name == "Gable Roof 30°");
            var walls = session.Document.Walls;
            var (wx0, wx1) = (walls.Min(w => Math.Min(w.Start.X, w.End.X)), walls.Max(w => Math.Max(w.Start.X, w.End.X)));
            var (wy0, wy1) = (walls.Min(w => Math.Min(w.Start.Y, w.End.Y)), walls.Max(w => Math.Max(w.Start.Y, w.End.Y)));
            session.AddSlabFromPreset(level, [new(wx0, wy0), new(wx1, wy0), new(wx1, wy1), new(wx0, wy1)], gable,
                session.SlabElevation(level, gable));
            var spiral = Guanacaste3D.Core.ArchitecturalLibrary.PresetsFor(Guanacaste3D.Core.StructuralElementKind.Stair)
                .Single(stair => stair.Style == Guanacaste3D.Core.ElementStyle.Spiral);
            var stairId = session.AddElementFromPreset(level, spiral, new(wx1 + 2.5, wy0 - 1.5), new(wx1 + 3.5, wy0 - 1.5));
            session.AddRailingsOnHost(stairId);
        }
        if (Environment.GetEnvironmentVariable("G3_SHOT_MEP") == "1") ApplyServices(session);
        if (Environment.GetEnvironmentVariable("G3_SHOT_SOLAR") == "1")
        {
            // Two arrays on the south slope of the roof, wired to an inverter on the west wall.
            var walls = session.Document.Walls;
            var (wx0, wx1) = (walls.Min(w => Math.Min(w.Start.X, w.End.X)), walls.Max(w => Math.Max(w.Start.X, w.End.X)));
            var (wy0, wy1) = (walls.Min(w => Math.Min(w.Start.Y, w.End.Y)), walls.Max(w => Math.Max(w.Start.Y, w.End.Y)));
            var southY = wy0 + (wy1 - wy0) * 0.27;
            foreach (var x in new[] { wx0 + (wx1 - wx0) * 0.3, wx0 + (wx1 - wx0) * 0.7 })
                session.AddElectricalDevice(level, Guanacaste3D.Core.ElectricalDeviceKind.SolarArray, "FV", new Guanacaste3D.Core.Point2(x, southY));
        }
        var mesh = Guanacaste3D.Core.Geometry.BuildingMesher.Build(session.Document, includeGround: true,
            plants: Environment.GetEnvironmentVariable("G3_SHOT_MODEL_TREES") == "1" ? Guanacaste3D.Core.Geometry.PlantDetail.Model : Guanacaste3D.Core.Geometry.PlantDetail.Render);
        var center = (min + max) / 2;
        if (Environment.GetEnvironmentVariable("G3_SHOT_TEXTURES") is { } textureFolder)
        {
            RenderTexture Load(string name) => RenderTexture.FromRaster(PngCodec.Load(Path.Combine(textureFolder, name + ".png")));
            // "none" keeps the built-in procedural materials, for close-ups of geometry.
            var materials = textureFolder == "none" ? [] : new Dictionary<Guanacaste3D.Core.Geometry.SurfaceKind, RenderMaterial>
            {
                [Guanacaste3D.Core.Geometry.SurfaceKind.Wall] = MaterialLibrary.Textured(Guanacaste3D.Core.Geometry.SurfaceKind.Wall, Load("brick-red"), 1.0, 0.6),
                [Guanacaste3D.Core.Geometry.SurfaceKind.Floor] = MaterialLibrary.Textured(Guanacaste3D.Core.Geometry.SurfaceKind.Floor, Load("stone-paving"), 2.0, 0.6),
            };
            var wide = Environment.GetEnvironmentVariable("G3_SHOT_WIDE") == "1";
            if (Environment.GetEnvironmentVariable("G3_SHOT_CLOSE") == "1")
            {
                var door = session.Document.Openings.First(opening => opening.Kind == Guanacaste3D.Core.OpeningKind.Door);
                var host = session.Document.Walls.Single(wall => wall.Id == door.WallId);
                var along = new Vector3((float)(host.End.X - host.Start.X), (float)(host.End.Y - host.Start.Y), 0) / (float)host.Length;
                var at = new Vector3((float)host.Start.X, (float)host.Start.Y, 0) + along * (float)door.Offset;
                var outward = new Vector3(along.Y, -along.X, 0);
                if (Vector3.Dot(outward, at - center) < 0) outward = -outward;
                return new BenchmarkScene("garden", mesh,
                    new RenderCamera(at + outward * 3.2f + along * 1.2f + new Vector3(0, 0, 1.5f), at + new Vector3(0, 0, 1.2f), 55)
                    {
                        CorrectVerticals = true
                    }, new RenderSettings()) { Materials = kind => materials.TryGetValue(kind, out var material) ? material : MaterialLibrary.ForKind(kind) };
            }
            return new BenchmarkScene("garden", mesh,
                wide
                    ? new RenderCamera(new Vector3(center.X - 2, min.Y - 30, 3.5f), new Vector3(center.X - 2, center.Y + 4, 5.5f), 60)
                    : new RenderCamera(new Vector3(center.X - 4, min.Y - 9, 1.9f), new Vector3(center.X - 1, center.Y, 1.6f), 62)
                {
                    CorrectVerticals = true
                }, new RenderSettings()) { Materials = kind => materials.TryGetValue(kind, out var material) ? material : MaterialLibrary.ForKind(kind) };
        }
        return new BenchmarkScene("garden", mesh,
            new RenderCamera(new Vector3(center.X - 4, min.Y - 17, 2.2f), new Vector3(center.X - 2, center.Y + 2, 3.2f), 62)
            {
                CorrectVerticals = true
            }, new RenderSettings());
    }
}
