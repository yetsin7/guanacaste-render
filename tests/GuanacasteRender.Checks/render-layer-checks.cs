using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the movie's layer animation: when levels exist, taking levels out of the house, and a video that shows it.</summary>
static class RenderLayerChecks
{
    public static void Run(Action<bool, string> check)
    {
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

        // A level that appears at 3 s is not there before; one that hides at 6 s is gone afterwards; others are untouched.
        var animation = new LayerAnimation();
        animation.Set(first, 3, true);
        animation.Set(first, 6, false);
        check(animation.HiddenAt(0).Contains(first) && animation.HiddenAt(2.9f).Contains(first) &&
              !animation.HiddenAt(3).Contains(first) && !animation.HiddenAt(5.9f).Contains(first) &&
              animation.HiddenAt(6).Contains(first) && animation.HiddenAt(100).Contains(first) && !animation.HiddenAt(0).Contains(second),
            "A level appears at its first key, hides at the next, and levels without keys are never hidden");
        animation.Set(first, 3.02f, false);
        check(animation.Keys.Count(key => key.Seconds < 4) == 1 && !animation.Keys[0].Visible,
            "A key set within 0.05 s of another of the same level replaces it");
        animation.KeepLevels([second]);
        check(animation.IsEmpty, "Keys of levels the project no longer has are forgotten");
        check(LayerAnimation.StateKey([first, second]) == LayerAnimation.StateKey([second, first]) &&
              LayerAnimation.StateKey([]) == "" && LayerAnimation.StateKey([first]) != LayerAnimation.StateKey([second]),
            "Equal sets of hidden levels have equal names, whatever their order");

        // The cobogó house: taking levels out leaves a valid, smaller model.
        var house = BreezeBlockHouseShowcase.Create();
        var levels = house.Levels.Select(level => level.Id).ToList();
        check(levels.Count >= 2, $"The cobogó house has more than one level to animate ({levels.Count})");
        var whole = BuildingMesher.Build(house, includeGround: true).TriangleCount;
        var random = new Random(20261104);
        var allSmaller = true;
        for (var trial = 0; trial < 12; trial++)
        {
            var hidden = levels.Where(_ => random.Next(2) == 0).ToHashSet();
            var copy = house.Clone();
            LevelVisibility.Hide(copy, hidden);
            var wallIds = copy.Walls.Select(wall => wall.Id).ToHashSet();
            var orphans = copy.Openings.Any(opening => !wallIds.Contains(opening.WallId));
            var triangles = BuildingMesher.Build(copy, includeGround: true).TriangleCount;
            if (orphans || triangles > whole || (hidden.Count > 0 && triangles == whole && house.Walls.Any(wall => hidden.Contains(wall.LevelId))))
                allSmaller = false;
        }
        check(allSmaller, "Hiding any set of levels of the cobogó house leaves no orphan openings and never a larger model");
        var upper = house.Levels.OrderBy(level => level.Elevation).Last().Id;
        var withoutUpper = house.Clone();
        LevelVisibility.Hide(withoutUpper, new HashSet<Guid> { upper });
        check(withoutUpper.Walls.Count < house.Walls.Count && withoutUpper.Walls.All(wall => wall.LevelId != upper) &&
              house.Walls.Any(wall => wall.LevelId == upper) && house.Walls.Count == BreezeBlockHouseShowcase.Create().Walls.Count,
            "Hiding the top level takes its walls out of the copy and leaves the project itself untouched");

        // A video in which the camera stands still and the upper level appears halfway through.
        var (min, max) = BuildingMesher.Build(house).Bounds();
        var camera = RenderCamera.FrameBounds(min, max);
        var path = new CameraPath();
        path.Add(new CameraMark(camera.Position, camera.Target, 45, 0));
        path.Add(new CameraMark(camera.Position, camera.Target, 45, 2));
        RenderScene SceneOf(BuildingDocument document) =>
            new RenderSceneBuilder().AddBuilding(BuildingMesher.Build(document, includeGround: true)).Build();
        var calls = new List<string>();
        RenderScene? SceneFor(IReadOnlySet<Guid> hidden)
        {
            calls.Add(LayerAnimation.StateKey(hidden));
            var copy = house.Clone();
            LevelVisibility.Hide(copy, hidden);
            return SceneOf(copy);
        }
        var rise = new LayerAnimation();
        rise.Set(upper, 1, true);
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-layers-" + Guid.NewGuid().ToString("N"));
        var control = Path.Combine(folder, "control");
        try
        {
            var result = TourRenderer.RenderAsync(SceneOf(house), path, new RenderSettings(), 96, 60, 4, 4, new ImageFinish(), folder, null,
                CancellationToken.None, new LayerSource(rise, SceneFor)).GetAwaiter().GetResult();
            var files = Directory.GetFiles(folder, "frame_*.png").Order().ToArray();
            check(result.FramesWritten == result.TotalFrames && files.Length == result.TotalFrames && result.TotalFrames >= 8,
                $"The layer-animated video writes every frame ({files.Length})");
            check(calls.Count == 2 && calls[0] != calls[1] && calls[0] != "" && calls[1] == "",
                $"The video builds one scene with the level hidden and one with it shown, not one per frame ({calls.Count} builds)");
            TourRenderer.RenderAsync(SceneOf(house), path, new RenderSettings(), 96, 60, 4, 4, new ImageFinish(), control, null,
                CancellationToken.None).GetAwaiter().GetResult();
            var plain = Directory.GetFiles(control, "frame_*.png").Order().ToArray();
            double Difference(string a, string b)
            {
                var (x, y) = (PngCodec.Load(a).Rgb, PngCodec.Load(b).Rgb);
                double total = 0;
                for (var index = 0; index < x.Length; index++) total += Math.Abs(x[index] - y[index]);
                return total / x.Length;
            }
            var change = Difference(files[0], files[^1]);
            var still = Difference(plain[0], plain[^1]);
            check(change > still + 0.5,
                $"With the camera still, the house changes between first and last frame only because the level appears ({change:0.00} against {still:0.00})");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        check(RenderFx.Find("layers") is { Planned: false, Category: FxCategory.Animation, Removable: true },
            "Layer animation is a usable effect of the Animation group");
    }
}
