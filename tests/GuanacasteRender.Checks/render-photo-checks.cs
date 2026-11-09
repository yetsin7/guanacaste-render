using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;

/// <summary>Photos (saved camera + style + filters + preview) and the Lumion-style travel speeds.</summary>
static class RenderPhotoChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new DocumentSession(ArchitecturalLibrary.CreateProject("Fotos"));
        var raised = 0;
        var undoDepth = session.UndoNames.Count;
        session.RenderPhotosChanged += (_, _) => raised++;
        var changedModel = 0;
        session.Changed += (_, _) => changedModel++;

        var first = new RenderSetup { CameraX = 3, CameraY = -4, CameraZ = 1.6, YawDegrees = 120, PitchDegrees = -5, StyleId = "none" };
        var altered = first with { ExposureStops = 1.2, Fog = 0.4, Bloom = true, Monochrome = true, SolarHour = 16 };
        var clean = altered.WithoutEffects();
        check(clean.ExposureStops == 0 && clean.Fog == 0 && !clean.Bloom && !clean.Monochrome &&
              clean.CameraX == altered.CameraX && clean.SolarHour == altered.SolarHour,
            "A new photo starts without image effects while preserving its camera and lighting");
        var transferred = clean.WithEffectsFrom(altered);
        check(transferred.ExposureStops == 1.2 && transferred.Fog == 0.4 && transferred.Bloom && transferred.Monochrome &&
              clean.ExposureStops == 0 && transferred.CameraX == clean.CameraX && transferred.SolarHour == clean.SolarHour,
            "Copying effects preserves the destination view and does not alter the source photo");
        var barkMesh = new SceneMesh();
        barkMesh.AddTriangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, SurfaceKind.Bark);
        barkMesh.Textures.Add(new SceneTexture(RenderTexture.FromRgba(1, 1, new byte[] { 120, 90, 60, 255 }), 0));
        barkMesh.TexturedTriangles[0] = new TexturedTriangle(Vector2.Zero, Vector2.UnitX, Vector2.UnitY, 0);
        var barkScene = new RenderSceneBuilder().AddBuilding(barkMesh).Build();
        var barkMaterial = barkScene.Materials[barkScene.TriangleMaterial[0]];
        check(barkMaterial.UvMapped && barkMaterial.Pattern == SurfacePattern.Bark && barkMaterial.NormalMap is null,
            "Textured bark without a normal map retains procedural relief");
        var id = session.AddRenderPhoto("Fachada", first, "AAAA");
        check(session.Document.RenderScenes.Count == 1 && session.Document.RenderScenes[0].Thumbnail == "AAAA",
            "A photo keeps its camera setup and preview");
        check(raised == 1 && changedModel == 0 && session.UndoNames.Count == undoDepth,
            "Adding a photo does not rebuild the model nor add an undo step");

        var warm = first with { LutPresetId = ColorLutPresets.All.First(p => p.Id != "none").Id, LutStrength = 0.9 };
        session.UpdateRenderPhoto(id, warm, thumbnail: "BBBB");
        var stored = session.Document.RenderScenes[0];
        check(stored.Setup.LutStrength == 0.9 && stored.Thumbnail == "BBBB" && stored.Name == "Fachada",
            "Editing a photo's filters replaces its setup and preview but keeps its name");
        session.UpdateRenderPhoto(id, name: "Fachada norte");
        check(session.Document.RenderScenes[0].Name == "Fachada norte" && session.Document.RenderScenes[0].Thumbnail == "BBBB",
            "Renaming a photo keeps its preview");

        session.UpdateRenderPhoto(id, setup: warm);
        check(session.Document.RenderScenes[0].Thumbnail == "BBBB",
            "Reapplying the same photo setup preserves its preview");
        session.UpdateRenderPhoto(id, setup: warm with { ExposureStops = 0.4 });
        check(session.Document.RenderScenes[0].Thumbnail is null,
            "Changing a photo's camera or filters without a new preview invalidates its stale thumbnail");
        session.UpdateRenderPhoto(id, setup: warm, thumbnail: "BBBB");

        session.AddRenderPhoto("Interior", first with { CameraX = 0 }, null, index: 0);
        check(session.Document.RenderScenes[0].Name == "Interior" && session.Document.RenderScenes[1].Id == id,
            "A duplicated photo can be inserted next to its source");

        var path = Path.Combine(Path.GetTempPath(), "g3d-photos-" + Guid.NewGuid().ToString("N") + ".g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var loaded = ProjectFile.Load(path).RenderScenes;
            check(loaded.Count == 2 && loaded[1].Thumbnail == "BBBB" && loaded[1].Setup.LutStrength == 0.9 && loaded[0].Thumbnail is null,
                "Photos with their previews survive a .g3d round trip");
        }
        finally { if (File.Exists(path)) File.Delete(path); }

        var rejected = false;
        try { session.UpdateRenderPhoto(id, thumbnail: new string('A', RenderSceneBookmark.MaximumThumbnailLength + 1)); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "An oversized preview is rejected before it can bloat the project");

        session.DeleteRenderPhoto(id);
        check(session.Document.RenderScenes.Count == 1 && session.Document.RenderScenes[0].Name == "Interior",
            "Deleting a photo removes only that photo");

        var camera = new NavigationCamera { Position = Vector3.Zero, YawDegrees = 0, PitchDegrees = 0 };
        var normal = CameraTravel.Displacement(camera, new Vector3(1, 0, 0), 2, 0.1f, false, 1f).Length();
        var fast = CameraTravel.Displacement(camera, new Vector3(1, 0, 0), 2, 0.1f, false, 4f).Length();
        var slow = CameraTravel.Displacement(camera, new Vector3(1, 0, 0), 2, 0.1f, false, 0.25f).Length();
        check(Math.Abs(fast / normal - 4) < 0.01 && Math.Abs(slow / normal - 0.25) < 0.01,
            "Shift flies four times faster and Space a quarter as fast, as in Lumion");
        check(CameraTravel.Displacement(camera, new Vector3(1, 0, 0), 2, 0.1f, false, float.NaN) == Vector3.Zero,
            "An invalid speed multiplier cannot move the camera");
        LensChecks(check);
    }

    /// <summary>Roll, lens shift and focus distance: what the shift lens must do to the picture, and what the setup accepts.</summary>
    private static void LensChecks(Action<bool, string> check)
    {
        var house = SampleProjects.CreateDemonstrationHouse();
        var scene = BuildingMesher.Build(house, includeGround: true);
        var (min, max) = BuildingMesher.Build(house).Bounds();
        var framed = RenderCamera.FrameBounds(min, max);
        const int width = 64, height = 40;
        var plain = new PathTracer(scene, width, height, framed).CaptureFeatures();
        var shifted = new PathTracer(scene, width, height, framed with { ShiftY = 0.25f }).CaptureFeatures();
        // A shift of a quarter of the frame slides the picture down by a quarter of the rows without changing perspective.
        var rows = height / 4;
        var same = 0;
        var compared = 0;
        for (var row = rows; row < height; row++)
            for (var column = 0; column < width; column++)
            {
                var a = shifted.Depth[row * width + column];
                var b = plain.Depth[(row - rows) * width + column];
                if (!float.IsFinite(a) || !float.IsFinite(b)) continue;
                compared++;
                if (Math.Abs(a - b) <= 0.02f * Math.Max(1, b)) same++;
            }
        check(compared > 100 && same > compared * 0.97, "A vertical lens shift moves the picture without changing its perspective");

        var rolled = new PathTracer(scene, width, height, framed with { RollDegrees = 30 }).CaptureFeatures();
        var middle = (height / 2) * width + width / 2;
        var differing = 0;
        for (var index = 0; index < plain.Depth.Length; index++)
            if (Math.Abs(plain.Depth[index] - rolled.Depth[index]) > 0.05f) differing++;
        check(Math.Abs(plain.Depth[middle] - rolled.Depth[middle]) < 0.5f && differing > plain.Depth.Length / 20,
            "Rolling the camera turns the picture about its centre");

        // A 360° panorama (odd sizes put a pixel exactly on the view axis): the middle looks where the camera does,
        // and the picture covers the sky as well as the building.
        const int pw = 65, ph = 33;
        var flat = new PathTracer(scene, pw, ph, framed).CaptureFeatures();
        var pano = new PathTracer(scene, pw, ph, framed with { Panoramic = true }).CaptureFeatures();
        var centre = (ph / 2) * pw + pw / 2;
        check(Math.Abs(pano.Depth[centre] - flat.Depth[centre]) < 0.02f * Math.Max(1, flat.Depth[centre]),
            "The middle of a panorama looks where the camera looks");
        var hits = pano.Depth.Count(float.IsFinite);
        check(hits > pano.Depth.Length / 20 && hits < pano.Depth.Length - pano.Depth.Length / 20,
            "A panorama shows the building and the sky around it");

        var lens = new RenderSetup { RollDegrees = 12, ShiftX = -0.1, ShiftY = 0.2, FocusDistance = 8 };
        lens.Validate();
        var rejected = 0;
        foreach (var bad in new[] { lens with { RollDegrees = 60 }, lens with { ShiftY = 0.8 }, lens with { ShiftX = double.NaN },
                     lens with { FocusDistance = -1 }, lens with { FNumber = 0.5 }, lens with { FNumber = 40 },
                     lens with { ContrastAdjust = 2 }, lens with { SaturationAdjust = -2 } })
        {
            try { bad.Validate(); }
            catch (InvalidDataException) { rejected++; }
        }
        check(rejected == 8, "Lens, aperture and grading settings outside their range are rejected");
        (lens with { FNumber = 2.8, ContrastAdjust = 0.3, SaturationAdjust = -0.2 }).Validate();
        (lens with { FNumber = 0 }).Validate();
        var opening = PhotographicCamera.ApertureRadius(50, 2);
        check(Math.Abs(opening - 0.0125f) < 1e-5 && PhotographicCamera.ApertureRadius(50, 8) < opening,
            "A wider aperture opens the lens more, which blurs what is out of focus");

        var focal35 = PhotographicCamera.VerticalFieldOfView(35);
        check(Math.Abs(PhotographicCamera.FocalLength(focal35) - 35) < 1e-3, "Focal length and field of view convert both ways");
    }
}
