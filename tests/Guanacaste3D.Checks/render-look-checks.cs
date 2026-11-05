using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using System.Numerics;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the picture styles, image effects, cloud types and the material library.</summary>
static class RenderLookChecks
{
    public static void Run(Action<bool, string> check)
    {
        const int width = 48, height = 32;
        var image = new Vector3[width * height];
        for (var index = 0; index < image.Length; index++)
        {
            var (x, y) = (index % width, index / width);
            image[index] = new Vector3(0.05f + 0.4f * x / width, 0.08f + 0.3f * y / height, 0.12f);
        }
        image[16 * width + 24] = new Vector3(60);
        var plain = new ImageFinish { Dither = false, Bloom = 0, Vignette = 0 };
        var neutral = ImageProcessor.ToBgra32(image, width, height, 1, plain);
        check(neutral.SequenceEqual(ImageProcessor.ToBgra32(image, width, height, 1, plain with { ShadowTint = Vector3.One })),
            "Effects left at their defaults leave the picture unchanged");

        foreach (var style in RenderLooks.Styles.Skip(1))
        {
            var styled = ImageProcessor.ToBgra32(image, width, height, 1, style.Apply(plain));
            check(styled.Length == neutral.Length && !styled.SequenceEqual(neutral), $"The {style.Id} style changes the picture");
        }
        check(RenderLooks.Styles.Select(style => style.Id).Distinct().Count() == RenderLooks.Styles.Count &&
              RenderLooks.Skies.All(sky => RenderLooks.Styles.Any(style => style.Id == sky.StyleId)),
            "Styles have unique identifiers and every sky names an existing style");

        check(RenderLooks.Skies.Count >= 30 && RenderLooks.Skies.Select(sky => sky.Id).Distinct().Count() == RenderLooks.Skies.Count &&
              Enum.GetValues<CloudStyle>().Length >= 8 && RenderLooks.Skies.All(sky =>
                  sky.DayOfYear is >= 1 and <= 366 && sky.SolarHour is >= 5 and <= 19 &&
                  sky.Turbidity is >= 1.7 and <= 10 && sky.CloudSeed <= 1023 &&
                  sky.CloudDirectionDegrees is >= -180 and <= 180 &&
                  sky.SunStrength is >= 0 and <= 2 && sky.SkyStrength is >= 0 and <= 2),
            "Thirty ready-to-use skies cover multiple weather families with bounded reproducible parameters");

        // A neutral .cube is the identity at full intensity; reversing its channels is a true color effect.
        var cube = "TITLE \"Identity\"\\nLUT_3D_SIZE 2\\nDOMAIN_MIN 0 0 0\\nDOMAIN_MAX 1 1 1\\n" +
            "0 0 0\\n1 0 0\\n0 1 0\\n1 1 0\\n0 0 1\\n1 0 1\\n0 1 1\\n1 1 1\\n";
        var lookup = ColorLookupTable.FromCube(cube.Replace("\\n", "\n"));
        check(lookup.Size == 2 && Vector3.Distance(lookup.Sample(new Vector3(.35f,.6f,.7f)),
              new Vector3(.35f,.6f,.7f)) < .0001f,
            "A standard .cube file interpolates RGB with red varying fastest");
        // Imported grading presets from other editors can carry a UTF-8 BOM,
        // lower-case directives and tab-separated titles. Their domain still
        // controls the input mapping, rather than being silently ignored.
        var compatibleCube = cube.Replace("\\n", "\n")
            .Replace("TITLE \"Identity\"", "\uFEFFtitle\t\"Identity\"", StringComparison.Ordinal)
            .Replace("LUT_3D_SIZE", "lut_3d_size", StringComparison.Ordinal)
            .Replace("DOMAIN_MIN 0 0 0", "domain_min -1 -1 -1", StringComparison.Ordinal)
            .Replace("DOMAIN_MAX 1 1 1", "domain_max 1 1 1", StringComparison.Ordinal);
        var compatibleLookup = ColorLookupTable.FromCube(compatibleCube);
        check(compatibleLookup.Size == 2 &&
              Vector3.Distance(compatibleLookup.Sample(Vector3.Zero), new Vector3(.5f)) < .0001f &&
              Vector3.Distance(compatibleLookup.Sample(-Vector3.One), Vector3.Zero) < .0001f &&
              Vector3.Distance(compatibleLookup.Sample(Vector3.One), Vector3.One) < .0001f,
            "External .cube files accept a UTF-8 BOM, mixed-case directives, tabs and custom input domains");
        var invalidDomainRejected = false;
        try
        {
            _ = ColorLookupTable.FromCube(compatibleCube.Replace(
                "domain_max 1 1 1", "domain_max -1 -1 -1", StringComparison.Ordinal));
        }
        catch (InvalidDataException) { invalidDomainRejected = true; }
        check(invalidDomainRejected, "A malformed imported LUT domain remains rejected");

        var sample = ColorLutPresets.Find("warm")!;
        var colorLut = ImageProcessor.ToBgra32(image, width, height, 1, plain with
        { Lut = sample, LutStrength = 1 });
        var colorLutOff = ImageProcessor.ToBgra32(image, width, height, 1, plain with
        { Lut = sample, LutStrength = 0 });
        check(!colorLut.SequenceEqual(neutral) && colorLutOff.SequenceEqual(neutral),
            "3D LUT filters modify the final photo and are exactly removable without rerendering");
        check(ColorLutPresets.All.Count >= 14 &&
              ColorLutPresets.All.Select(preset => preset.Id).Distinct().Count() == ColorLutPresets.All.Count &&
              ColorLutPresets.All.All(preset => preset.Map.Size == 17),
            "Offline LUT library contains 14 named presets with deterministic lookup tables");
        var badCube = false;
        try { _ = ColorLookupTable.FromCube("LUT_3D_SIZE 64\\n0 0 0".Replace("\\n", "\n")); }
        catch (InvalidDataException) { badCube = true; }
        check(badCube, "Malformed LUTs cannot allocate an unbounded 3D cube");

        var lutProject = BuildingDocument.Create("Color filters");
        lutProject.RenderSetup = new RenderSetup
        {
            LutPresetId = "custom", LutPath = "assets/film-look.cube", LutStrength = .65
        };
        var lutFile = Path.Combine(Path.GetTempPath(), "guanacaste-lut-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(lutFile, lutProject);
            var reopened = ProjectFile.Load(lutFile).RenderSetup;
            check(reopened?.LutPresetId == "custom" &&
                  reopened.LutPath == "assets/film-look.cube" &&
                  reopened.LutStrength == .65,
                "Imported .cube references and LUT intensity survive native .g3d save/reopen");
        }
        finally
        {
            if (File.Exists(lutFile)) File.Delete(lutFile);
            if (File.Exists(lutFile + ".bak")) File.Delete(lutFile + ".bak");
        }

        check(RenderSetup.SelectedCustomLutPath("custom", "assets/film-look.cube") == "assets/film-look.cube" &&
              RenderSetup.SelectedCustomLutPath("warm", "assets/film-look.cube") is null &&
              RenderSetup.SelectedCustomLutPath("none", "assets/film-look.cube") is null,
            "Switching from a custom LUT to a built-in filter never reloads or stores the old .cube path");

        var mono = ImageProcessor.ToBgra32(image, width, height, 1, plain with { Monochrome = true });
        check(Enumerable.Range(0, width * height).All(pixel => mono[pixel * 4] == mono[pixel * 4 + 1] && mono[pixel * 4 + 1] == mono[pixel * 4 + 2]),
            "Black and white has equal channels");

        // A star streak lights the row and column of a bright point, farther than bloom reaches.
        var glare = ImageProcessor.ToBgra32(image, width, height, 1, plain with { Glare = 1 });
        int Green(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4 + 1];
        check(Green(glare, 4, 16) > Green(neutral, 4, 16) && Green(glare, 24, 2) > Green(neutral, 24, 2) &&
              Green(glare, 4, 2) <= Green(neutral, 4, 2) + 1,
            "Glare draws streaks along the bright point's row and column only");

        // Fog: a far pixel drifts toward the fog colour, a near one barely changes.
        var features = new FeatureBuffer(width, height);
        for (var index = 0; index < features.Depth.Length; index++) features.Depth[index] = index % width < width / 2 ? 2f : 300f;
        var foggy = ImageProcessor.ToBgra32(image, width, height, 1, plain with { Fog = 1 }, () => features);
        int Blue(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4];
        check(Math.Abs(Blue(foggy, 2, 5) - Blue(neutral, 2, 5)) <= 3 && Blue(foggy, 40, 5) > Blue(neutral, 40, 5) + 20,
            "Fog thickens with distance");

        var sharp = ImageProcessor.ToBgra32(image, width, height, 1, plain with { Sharpen = 1, FilmGrain = 1, ChromaticAberration = 1 });
        check(!sharp.SequenceEqual(neutral) && sharp.Length == neutral.Length, "Sharpening, grain and aberration run together");

        // Clouds: every type covers part of a partly cloudy sky; cirrus is thin, storm clouds are deep.
        foreach (var style in Enum.GetValues<CloudStyle>())
        {
            var layer = new CloudLayer(0.6f, 7, style);
            var samples = Enumerable.Range(0, 400).Select(index =>
            {
                var azimuth = index * 0.37f;
                var elevation = 0.2f + index % 20 * 0.04f;
                return layer.Sample(Vector3.Normalize(new Vector3(MathF.Cos(azimuth), MathF.Sin(azimuth), elevation)));
            }).ToArray();
            var covered = samples.Count(sample => sample.Opacity > 0.05f);
            check(covered is > 10 and < 400, $"{style} clouds cover part of the sky");
            if (style == CloudStyle.Cirrus) check(samples.Max(sample => sample.Thickness) <= 0.2f, "Cirrus stays thin");
            if (style == CloudStyle.Storm) check(samples.Max(sample => sample.Thickness) > 0.9f, "Storm clouds are deep");
        }

        var variableClouds = new CloudLayer(0.7f, 39, CloudStyle.Altocumulus, 30);
        var direction = Vector3.Normalize(new Vector3(0.65f, 0.7f, 0.55f));
        var first = variableClouds.Sample(direction);
        var invalidDirections = new[]
        {
            new Vector3(float.NaN, 0, 1),
            new Vector3(0, float.PositiveInfinity, 1),
            new Vector3(0, 0, float.NaN),
            new Vector3(0, 0, float.PositiveInfinity),
            new Vector3(float.NegativeInfinity, 0, 1),
            // Finite, but not valid sky rays: projection can overflow or leave the noise grid.
            new Vector3(float.MaxValue, 0, 1),
            new Vector3(1e20f, -1e20f, 1),
            new Vector3(1e7f, 0, 1)
        };
        check(invalidDirections.All(ray => variableClouds.Sample(ray) == (0f, 0f)) &&
              variableClouds.Sample(new Vector3(0, 0, -1)) == (0f, 0f) &&
              float.IsFinite(first.Opacity) && float.IsFinite(first.Thickness),
            "Cloud sampling rejects invalid sky rays without propagating NaN into rendered pixels");
        check(first == new CloudLayer(0.7f, 39, CloudStyle.Altocumulus, 30).Sample(direction),
            "Cloud seeds and wind rotation reproduce the same cloud shape");
        var samplesA = Enumerable.Range(0, 100).Select(i =>
        {
            var a = i * .33f;
            return variableClouds.Sample(Vector3.Normalize(new Vector3(MathF.Cos(a), MathF.Sin(a), .4f))).Opacity;
        }).ToArray();
        var samplesB = Enumerable.Range(0, 100).Select(i =>
        {
            var a = i * .33f;
            return new CloudLayer(0.7f, 87, CloudStyle.Altocumulus, -50)
                .Sample(Vector3.Normalize(new Vector3(MathF.Cos(a), MathF.Sin(a), .4f))).Opacity;
        }).ToArray();
        check(samplesA.Zip(samplesB).Count(pair => Math.Abs(pair.First - pair.Second) > .001f) >= 10,
            "Cloud seed and wind direction create distinct usable skies without downloading assets");

        var clearSun = new CloudLayer(0, 7, CloudStyle.Cumulus);
        var shadowSun = Vector3.Normalize(new Vector3(.3f, .2f, .9f));
        check(clearSun.SunTransmittance(Vector3.Zero, shadowSun) == 1f &&
              new CloudLayer(.8f, 7, CloudStyle.Storm)
                  .SunTransmittance(new Vector3(0, 0, 1500), shadowSun) == 1f,
            "Clear skies and positions above the clouds do not lose sunlight");
        var castClouds = new CloudLayer(.65f, 39, CloudStyle.Cumulonimbus, -50);
        var cloudShadows = Enumerable.Range(0, 625).Select(i =>
            castClouds.SunTransmittance(new Vector3((i % 25 - 12) * 350, (i / 25 - 12) * 350, 0), shadowSun)).ToArray();
        check(cloudShadows.All(t => float.IsFinite(t) && t is >= .05f and <= 1f) &&
              cloudShadows.Min() < .97f && cloudShadows.Max() - cloudShadows.Min() > .04f,
            "Procedural clouds cast spatially varying, bounded shadows on the model and site");
        var shadowPoint = new Vector3(280, -90, 0);
        check(castClouds.SunTransmittance(shadowPoint, shadowSun) ==
              new CloudLayer(.65f, 39, CloudStyle.Cumulonimbus, -50).SunTransmittance(shadowPoint, shadowSun),
            "Cloud shadow positions reproduce exactly for the same seed and wind");

        var atmosphereSetup = new RenderSetup
        {
            SkyPresetId = "tropical-dawn", Clouds = CloudStyle.Cumulonimbus,
            CloudSeed = 73, CloudDirectionDegrees = -65, Turbidity = 5.3,
            SunStrength = .45, SkyStrength = 1.5
        };
        atmosphereSetup.Validate();
        var savedSkyPath = Path.Combine(Path.GetTempPath(), "guanacaste-sky-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            var skyProject = BuildingDocument.Create("Built-in atmosphere");
            skyProject.RenderSetup = atmosphereSetup;
            ProjectFile.Save(savedSkyPath, skyProject);
            var restored = ProjectFile.Load(savedSkyPath).RenderSetup;
            check(restored == atmosphereSetup,
                "Sky preset ID, cloud shape/seed/rotation, turbidity and light ratios survive the .g3d round trip");
        }
        finally
        {
            if (File.Exists(savedSkyPath)) File.Delete(savedSkyPath);
            if (File.Exists(savedSkyPath + ".bak")) File.Delete(savedSkyPath + ".bak");
        }
        var invalidSky = false;
        try { (atmosphereSetup with { CloudDirectionDegrees = double.NaN }).Validate(); }
        catch (InvalidDataException) { invalidSky = true; }
        check(invalidSky, "Invalid atmospheric parameters are rejected before corrupting project files");

        // Picking a slab in the render editor must target the material that the
        // geometry builder actually used, not always the generic Floor category.
        var slab = new FloorSlab(Guid.NewGuid(), Guid.NewGuid(), "Material selection",
            [new Point2(0, 0), new Point2(4, 0), new Point2(4, 4), new Point2(0, 4)], 0.2);
        check(BuildingMesher.SelectableSlabSurface(slab) == SurfaceKind.Floor &&
              BuildingMesher.SelectableSlabSurface(slab with { Kind = FloorKind.Water }) == SurfaceKind.Water &&
              BuildingMesher.SelectableSlabSurface(slab with { Kind = FloorKind.Toposolid }) == SurfaceKind.Ground,
            "Render picking resolves ordinary slabs, pools and topography to their actual materials");
        check(BuildingMesher.SelectableSlabSurface(slab with
              { Kind = FloorKind.Roof, Shape = RoofShape.Gable, Pitch = 30 }) == SurfaceKind.Roof &&
              BuildingMesher.SelectableSlabSurface(slab with
              { Kind = FloorKind.Ceiling, Finish = FinishKind.Paint }) == SurfaceKind.Wall &&
              BuildingMesher.SelectableSlabSurface(slab with
              { Kind = FloorKind.Floor, Finish = FinishKind.ExposedConcrete }) == SurfaceKind.WallConcrete,
            "Render picking respects pitched roofs and the assigned slab finish");

        // Materials.
        var presets = MaterialPresets.All;
        check(presets.Any(preset => MaterialCatalogSearch.Matches("ceramica", preset.Name)) &&
              presets.Any(preset => MaterialCatalogSearch.Matches("marmol", preset.Name)) &&
              MaterialCatalogSearch.Matches("ROBLE", "Roble claro") &&
              MaterialCatalogSearch.Matches("oak-light", "Roble claro", "oak-light"),
            "Spanish material search ignores accents and case while retaining stable id lookup");
        check(MaterialCatalogSearch.Matches("mate pintura", "Pintura mate blanca", "paint-matte-white") &&
              MaterialCatalogSearch.Matches("  madera   clara  ", "Madera clara") &&
              MaterialCatalogSearch.Matches("", "Cualquier material") &&
              !MaterialCatalogSearch.Matches("acero azul", "Acero inoxidable", "metal-steel") &&
              !MaterialCatalogSearch.Matches("ceramica", "Hormigón", "concrete"),
            "Material search matches all words across fields without matching unrelated finishes");
        check(presets.Count >= 60 && presets.Select(preset => preset.Id).Distinct().Count() == presets.Count &&
              presets.All(preset => preset.Material.Roughness is >= 0 and <= 1 && preset.Material.Metallic is >= 0 and <= 1 &&
                  preset.TextureSize > 0 && Enum.IsDefined(preset.Group)),
            "The material library has 60+ presets with unique ids and physical values");
        check(presets.Count >= 120 && presets.Count(preset => preset.Id.StartsWith("paint-satin-", StringComparison.Ordinal)) >= 12 &&
              presets.Count(preset => preset.Id.StartsWith("fabric-woven-", StringComparison.Ordinal)) >= 12,
            "Expanded library includes 120+ presets and complete finish colour variants");
        check(RenderLooks.Styles.Count >= 20 &&
              RenderLooks.Styles.Select(style => style.Id).Distinct().Count() == RenderLooks.Styles.Count,
            "Architectural filter catalog includes at least twenty uniquely identified looks");
        check(Enum.GetValues<MaterialFamily>().All(group => presets.Any(preset => preset.Group == group)),
            "Every material family has presets");
        // Metals must be told apart on their balls: every pair of the common ones differs clearly, a mirror has a crisp horizon and a
        // brushed metal shows streaks, which the plain ones do not.
        var metalIds = new[] { "chrome", "metal-steel", "metal-iron", "metal-lead", "metal-pearl", "aluminium-natural", "copper", "brass", "metal-gold", "zinc", "steel-brushed" };
        var balls = metalIds.ToDictionary(id => id, id => MaterialBall.Render(MaterialPresets.FromPath(MaterialPresets.Prefix + id)!.Material, 64));
        double Gap(string a, string b)
        {
            double sum = 0;
            var count = 0;
            for (var i = 0; i < balls[a].Length; i += 4)
            {
                if (balls[a][i + 3] < 255 || balls[b][i + 3] < 255) continue;
                sum += Math.Abs(balls[a][i] - balls[b][i]) + Math.Abs(balls[a][i + 1] - balls[b][i + 1]) + Math.Abs(balls[a][i + 2] - balls[b][i + 2]);
                count++;
            }
            return sum / count / 3;
        }
        var closest = metalIds.SelectMany((a, i) => metalIds.Skip(i + 1).Select(b => (a, b, gap: Gap(a, b)))).OrderBy(pair => pair.gap).First();
        check(closest.gap > 5,
            $"The common metals look different on their balls: the two nearest ({closest.a} and {closest.b}) still differ by {closest.gap:0.0} levels");
        var oak = MaterialPresets.FromPath(MaterialPresets.Prefix + "oak-light")!;
        var chrome = MaterialPresets.FromPath(MaterialPresets.Prefix + "chrome")!;
        var texture = RenderTexture.FromRaster(new RasterImage(2, 2, [200, 150, 100, 200, 150, 100, 200, 150, 100, 200, 150, 100]));
        check(MaterialPresets.Build(oak, texture).Texture == texture && MaterialPresets.Build(oak, null).Texture is null &&
              MaterialPresets.Build(chrome, null).Metallic == 1 && MaterialPresets.FromPath("builtin:concrete") is null,
            "Presets resolve with or without their texture and are told apart from plain textures");

        var setup = new RenderSetup { StyleId = "cinematic", Clouds = CloudStyle.Cirrus, Glare = 0.4, Fog = 0.2 };
        setup.Validate();
        var rejected = false;
        try { (setup with { StyleId = "no-such-style" }).Validate(); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "A saved render setup must name an existing style");

        // Material editing must preserve independent scale and relief through undo and native persistence.
        var materialsSession = new DocumentSession(BuildingDocument.Create("Material editing"));
        var edited = new RenderMaterialAssignment(SurfaceKind.Wall, MaterialPresets.Prefix + "oak-light")
        {
            TextureSize = 0.75, Relief = 0.65
        };
        materialsSession.SetRenderMaterial(edited);
        var initialUndoDepth = materialsSession.UndoNames.Count;
        var originalDocument = materialsSession.Document;
        var redundantChangeEvents = 0;
        materialsSession.Changed += (_, _) => redundantChangeEvents++;
        materialsSession.SetRenderMaterial(edited with { });
        check(ReferenceEquals(originalDocument, materialsSession.Document) &&
              materialsSession.UndoNames.Count == initialUndoDepth && redundantChangeEvents == 0,
            "Reapplying an identical material preserves document identity, undo depth and render notifications");
        check(materialsSession.Document.RenderMaterials.Single() == edited,
            "A custom material scale and relief are stored together");
        materialsSession.Undo();
        check(materialsSession.Document.RenderMaterials.Count == 0,
            "Undo restores the stock material after an edit");
        materialsSession.Redo();
        check(materialsSession.Document.RenderMaterials.Single() == edited,
            "Redo restores the material and its physical texture settings");
        var invalidReliefRejected = false;
        try { materialsSession.SetRenderMaterial(edited with { Relief = 1.5 }); }
        catch (InvalidDataException) { invalidReliefRejected = true; }
        check(invalidReliefRejected && materialsSession.Document.RenderMaterials.Single() == edited,
            "Out-of-range relief is rejected without changing the document");
        var materialPath = Path.Combine(Path.GetTempPath(), "guanacaste-material-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(materialPath, materialsSession.Document);
            var reopened = ProjectFile.Load(materialPath);
            check(reopened.RenderMaterials.Single() == edited,
                "Native project round trip preserves the edited material and relief");
        }
        finally
        {
            if (File.Exists(materialPath)) File.Delete(materialPath);
            if (File.Exists(materialPath + ".bak")) File.Delete(materialPath + ".bak");
        }
    }
}
