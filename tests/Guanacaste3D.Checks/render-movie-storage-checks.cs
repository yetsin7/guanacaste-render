using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Navigation;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks that the movie (camera marks and layer keys) survives as the text the project keeps.</summary>
static class RenderMovieStorageChecks
{
    public static void Run(Action<bool, string> check)
    {
        var level = Guid.NewGuid();
        var path = new CameraPath { Ease = false };
        path.Add(new CameraMark(new Vector3(1, 2, 3), new Vector3(4, 5, 6), 50, 0, "Entrada"));
        path.Add(new CameraMark(new Vector3(-7.25f, 8, 1.6f), new Vector3(0, 0, 1), 35, 3.5f));
        var layers = new LayerAnimation();
        layers.Set(level, 1.5f, true);
        layers.Set(level, 6, false);

        var text = MovieStorage.Serialize(path, layers);
        check(text is not null && MovieStorage.TryParse(text, out var back, out var backLayers) && !back.Ease &&
              back.Marks.Count == 2 && back.Marks[0] == path.Marks[0] && back.Marks[1] == path.Marks[1] &&
              backLayers.Keys.SequenceEqual(layers.Keys),
            "A movie reads back exactly: marks, names, times, field of view, easing and layer keys");
        check(MovieStorage.Serialize(new CameraPath(), new LayerAnimation()) is null, "An empty movie keeps nothing in the project");
        check(MovieStorage.TryParse(null, out var none, out var noLayers) && none.Marks.Count == 0 && noLayers.IsEmpty,
            "A project without a saved movie opens with an empty one");
        check(ProjectExtensions.IsValidKey(MovieStorage.Key) && text!.Length < ProjectExtensions.MaximumValueLength,
            "The movie's key and size fit what the project accepts");

        // Anything that does not fit is refused whole, never half loaded.
        string[] bad =
        [
            "no es json", "{}", """{"V":2,"Ease":true,"Marks":[],"Layers":[]}""",
            """{"V":1,"Ease":true,"Marks":[{"P":[1,2],"T":[0,0,0],"Fov":50,"S":1,"N":null}],"Layers":[]}""",
            """{"V":1,"Ease":true,"Marks":[{"P":[1,2,3],"T":[0,0,0],"Fov":500,"S":1,"N":null}],"Layers":[]}""",
            """{"V":1,"Ease":true,"Marks":[],"Layers":[{"L":"no-es-guid","S":1,"V":true}]}""",
            """{"V":1,"Ease":true,"Marks":[],"Layers":[{"L":"%N","S":-3,"V":true}]}""".Replace("%N", level.ToString("N"))
        ];
        check(bad.All(item => !MovieStorage.TryParse(item, out var path2, out var layers2) && path2.Marks.Count == 0 && layers2.IsEmpty),
            "Damaged or foreign movie text is refused and leaves an empty movie");

        // Many marks and keys, as a long movie would have.
        var long_ = new CameraPath();
        for (var index = 0; index < 120; index++)
            long_.Add(new CameraMark(new Vector3(index, -index, 1.6f), new Vector3(0, 0, 1.6f), 45, 1.25f));
        var many = new LayerAnimation();
        var random = new Random(20261104);
        for (var index = 0; index < 200; index++) many.Set(Guid.NewGuid(), (float)(random.NextDouble() * 120), random.Next(2) == 0);
        var big = MovieStorage.Serialize(long_, many);
        check(big is not null && big.Length < ProjectExtensions.MaximumValueLength &&
              MovieStorage.TryParse(big, out var bigBack, out var bigLayers) && bigBack.Marks.Count == 120 && bigLayers.Keys.Count == many.Keys.Count,
            "A long movie of 120 marks and 200 layer keys fits in the project and reads back whole");
    }
}
