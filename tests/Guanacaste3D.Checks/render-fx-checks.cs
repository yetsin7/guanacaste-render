using System.Text.RegularExpressions;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks the Render panel's effect catalog and saved styles.</summary>
static class RenderFxChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(RenderFx.All.Select(effect => effect.Id).Distinct().Count() == RenderFx.All.Count,
            "Every render effect has its own id");
        check(Enum.GetValues<FxCategory>().All(category => RenderFx.In(category).Any()),
            "Every effect category lists at least one effect");

        var fog = RenderFx.Find("fog")!;
        check(fog.IsNeutral(_ => 0) && !fog.IsNeutral(_ => 0.4) && fog.IsNeutral(_ => null),
            "An effect is neutral only while all of its settings sit at their neutral value");
        check(RenderFx.All.Where(effect => effect.Removable && !effect.Planned)
                .All(effect => !effect.IsNeutral(key => effect.Controls.Single(control => control.Key == key).StartValue)),
            "Adding a finishing effect starts it off neutral so it visibly does something");

        var xaml = FindPanelXaml();
        if (xaml is not null)
        {
            var names = Regex.Matches(File.ReadAllText(xaml), "x:Name=\"([A-Za-z0-9_]+)\"").Select(match => match.Groups[1].Value).ToHashSet();
            var missing = RenderFx.All.SelectMany(effect => effect.Controls).Select(control => control.Key).Where(key => !names.Contains(key)).ToList();
            check(missing.Count == 0, "Every setting an effect edits exists in the Render panel" +
                (missing.Count == 0 ? "" : ": " + string.Join(", ", missing)));
        }

        var style = new FxStyle("Cálido", ["fog", "glare"], new() { ["FogSlider"] = 0.3, ["GlareSlider"] = 0.5 });
        var back = FxStyle.TryParse(style.ToJson());
        check(back is { Name: "Cálido" } && back.Effects.SequenceEqual(style.Effects) && back.Values["FogSlider"] == 0.3,
            "A saved effect style reads back the same");
        check(FxStyle.TryParse("{}") is null && FxStyle.TryParse("no es json") is null &&
              FxStyle.TryParse("""{"Version":9,"Name":"x","Effects":[],"Values":{}}""") is null &&
              FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["inventado"],"Values":{}}""") is null,
            "A style file with the wrong shape, version or unknown effects is rejected");

        check(FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["fog","fog"],"Values":{"FogSlider":0.3}}""") is null &&
              FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["layers"],"Values":{"LayersAnimationCheck":1}}""") is not null &&
              FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["highlight"],"Values":{"HighlightPicker":2}}""") is null &&
              FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["highlight"],"Values":{"HighlightDimSlider":0.5}}""") is not null &&
              FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["sun"],"Values":{"HourSlider":12}}""") is null,
            "An imported FX style rejects duplicate and scene-only effects and accepts the animation effect");
        check(FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["fog"],"Values":{"GlareSlider":0.5}}""") is null &&
              FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["fog"],"Values":{"FogSlider":0.3,"FogSlider":0.8}}""") is null &&
              FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["fog"],"Values":{"FogSlider":"0.3"}}""") is null,
            "An FX style cannot silently ignore other effects' controls, duplicate values or nonnumeric values");
        check(FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":["fog"],"Values":{"FogSlider":1e999}}""") is null &&
              FxStyle.TryParse("""{"Version":1,"Name":"x","Effects":{},"Values":{}}""") is null &&
              FxStyle.TryParse(new string('x', 256 * 1024 + 1)) is null &&
              FxStyle.TryParse(new FxStyle(new string('x', 121), ["fog"], new()).ToJson()) is null,
            "Malformed, oversized and non-finite FX style data is rejected before changing the render");

    }

    /// <summary>The panel's XAML, found by walking up from the test output; null when the sources are not beside it.</summary>
    static string? FindPanelXaml()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "src", "Guanacaste3D.Desktop", "controls", "render-panel.xaml");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
