using System.Text.Json;

namespace Guanacaste3D.Core.Rendering;

/// <summary>The groups of the effect picker, in the order Lumion lists them.</summary>
public enum FxCategory { Featured, Sun, Weather, Sky, Objects, Camera, Animation, Artistic1, Artistic2, Advanced }

public enum FxControlKind { Slider, Toggle, Choice }

/// <summary>
/// One setting of an effect. <see cref="Key"/> is the <c>x:Name</c> of the Render panel control that holds the value, so the
/// stack edits the same sliders the render reads and nothing is stored twice. <see cref="Neutral"/> is the value at which
/// the setting does nothing (a toggle's 0 is off, 1 is on); <see cref="Start"/> is what a freshly added effect begins with.
/// </summary>
public sealed record FxControl(string Key, string Label, FxControlKind Kind, double Neutral, double? Start = null)
{
    /// <summary>The value given when the effect is first added to the stack, so adding it visibly does something.</summary>
    public double StartValue => Start ?? Neutral;
}

/// <summary>
/// An entry of the effect picker. A finishing effect is reset when it is muted or removed from the stack. A scene effect
/// (sun, sky, clouds) is not removable: it has no eye, and leaving the stack keeps its settings. A planned effect is shown
/// in the picker but is not usable yet.
/// </summary>
public sealed record FxEffect(string Id, string Name, string Description, FxCategory Category, string Glyph,
    IReadOnlyList<FxControl> Controls, bool Removable = true, bool Featured = false, bool Planned = false)
{
    /// <summary>Whether every setting sits at the value where the effect changes nothing.</summary>
    public bool IsNeutral(Func<string, double?> read) =>
        Controls.All(control => read(control.Key) is not { } value || Math.Abs(value - control.Neutral) < 1e-6);
}

/// <summary>The effects the Render panel offers, with the settings each one edits.</summary>
public static class RenderFx
{
    private static FxControl Slider(string key, string label, double neutral = 0, double? start = null) =>
        new(key, label, FxControlKind.Slider, neutral, start);

    /// <summary>A list pick; the value is the position in the list, and 0 is the entry that does nothing.</summary>
    private static FxControl Choice(string key, string label, double start = 0) =>
        new(key, label, FxControlKind.Choice, 0, start);

    private static FxControl Toggle(string key, string label, bool neutral) =>
        new(key, label, FxControlKind.Toggle, neutral ? 1 : 0, 1);

    public static IReadOnlyList<FxEffect> All { get; } =
    [
        new("color", "Corrección de color", "Luces, sombras, contraste, saturación y matiz", FxCategory.Artistic1, "",
            [Slider("HighlightsSlider", "Luces"), Slider("ShadowsSlider", "Sombras"), Slider("ContrastSlider", "Contraste", start: 0.1),
             Slider("SaturationSlider", "Saturación", start: 0.1), Slider("VibranceSlider", "Viveza", start: 0.2), Slider("TintSlider", "Matiz")], Featured: true),
        new("exposure", "Exposición y balance", "Brillo de la imagen y temperatura del color", FxCategory.Advanced, "",
            [Slider("ExposureSlider", "Exposición (EV)", start: 0.3), Slider("TemperatureSlider", "Temperatura"),
             Toggle("AutoExposureCheck", "Exposición automática", neutral: true)], Featured: true),
        new("glare", "Resplandor", "Halo en las luces fuertes", FxCategory.Artistic1, "",
            [Slider("GlareSlider", "Intensidad", start: 0.3)], Featured: true),
        new("bloom", "Bloom", "Brillo suave alrededor de las zonas claras", FxCategory.Artistic1, "",
            [Toggle("BloomCheck", "Activado", neutral: false)]),
        new("fog", "Niebla", "Velo atmosférico que suaviza la distancia", FxCategory.Weather, "",
            [Slider("FogSlider", "Densidad", start: 0.2)], Featured: true),
        new("rain", "Lluvia", "Lluvia inclinada y una luz más gris y apagada", FxCategory.Weather, "",
            [Slider("RainSlider", "Intensidad", start: 0.5)], Featured: true),
        new("snow", "Nieve", "Copos que caen y nieve blanca sobre lo que mira hacia arriba", FxCategory.Weather, "",
            [Slider("SnowSlider", "Intensidad", start: 0.6)], Featured: true),
        new("wet", "Suelo mojado", "El suelo bajo el cielo abierto se oscurece, brilla y forma charcos con ondas de lluvia", FxCategory.Weather, "",
            [Slider("WetnessSlider", "Intensidad", start: 0.6)], Featured: true),
        new("grass", "Hierba alta", "El césped y la hierba del suelo crecen con hojas de verdad, de un césped segado a un prado", FxCategory.Objects, "",
            [Slider("GrassSlider", "Altura", start: 0.4)], Featured: true),
        new("wind", "Viento", "La hierba se inclina con el viento y las ráfagas la recorren en ondas; se mueve con la película", FxCategory.Weather, "",
            [Slider("WindSlider", "Fuerza", start: 0.5)], Featured: true),
        new("vignette", "Viñeta", "Oscurece las esquinas de la imagen", FxCategory.Camera, "",
            [Toggle("VignetteCheck", "Activada", neutral: false)], Featured: true),
        new("dof", "Profundidad de campo", "Desenfoca lo que está lejos o cerca de la distancia de enfoque", FxCategory.Camera, "",
            [Slider("DepthOfFieldSlider", "Desenfoque", start: 30), Slider("FocusSlider", "Distancia de enfoque (0 = automática)")], Featured: true),
        new("tiltshift", "Tilt-shift", "Efecto miniatura: una franja nítida y el resto desenfocado", FxCategory.Camera, "",
            [Slider("TiltShiftSlider", "Intensidad", start: 0.7), Slider("TiltShiftPositionSlider", "Posición de la franja")]),
        new("lensflare", "Destello de lente", "Fantasmas y anillos de la luz más brillante, como en una lente real", FxCategory.Camera, "",
            [Slider("LensFlareSlider", "Intensidad", start: 0.6)], Featured: true),
        new("lightrays", "Rayos de luz", "Haces de luz que salen de la luz más brillante", FxCategory.Sun, "",
            [Slider("LightRaysSlider", "Intensidad", start: 0.6)], Featured: true),
        new("sharpen", "Nitidez", "Realza el detalle fino", FxCategory.Camera, "",
            [Slider("SharpenSlider", "Cantidad", start: 0.3)]),
        new("aberration", "Aberración cromática", "Franjas de color en los bordes, como una lente real", FxCategory.Camera, "",
            [Slider("AberrationSlider", "Cantidad", start: 0.2)]),
        new("grain", "Grano de película", "Ruido fino de película fotográfica", FxCategory.Artistic2, "",
            [Slider("GrainSlider", "Cantidad", start: 0.2)]),
        new("monochrome", "Blanco y negro", "Convierte la imagen a escala de grises", FxCategory.Artistic2, "",
            [Toggle("MonochromeCheck", "Activado", neutral: false)]),

        new("sun", "Sol", "Hora del día y potencia de la luz solar", FxCategory.Sun, "",
            [Slider("HourSlider", "Hora", 12), Slider("SunStrengthSlider", "Potencia solar", 1)], Removable: false, Featured: true),
        new("skylight", "Luz del cielo", "Cuánta luz ambiental aporta el cielo", FxCategory.Sky, "",
            [Slider("SkyStrengthSlider", "Intensidad", 1)], Removable: false),
        new("clouds", "Nubes y atmósfera", "Cobertura, dirección y turbidez del cielo", FxCategory.Sky, "",
            [Slider("CloudSlider", "Cobertura (%)", 30), Slider("CloudDirectionSlider", "Dirección"), Slider("TurbiditySlider", "Turbidez", 2.8)],
            Removable: false, Featured: true),

        new("highlight", "Resaltar material", "Destaca un material con un contorno y atenúa el resto de la imagen", FxCategory.Objects, "",
            [Choice("HighlightPicker", "Material", start: 1), Slider("HighlightDimSlider", "Atenuar el resto", start: 0.6)], Featured: true),
        new("ocean", "Océano", "Un mar con olas que sustituye al suelo infinito y llega hasta el horizonte", FxCategory.Objects, "",
            [Toggle("OceanCheck", "Activado", neutral: false), Slider("OceanLevelSlider", "Nivel del agua (m)", -0.5, -0.5),
             Slider("OceanWavesSlider", "Tamaño de las olas", 1, 1), Choice("OceanTintPicker", "Color del agua")], Featured: true),
        new("layers", "Animación de capas", "Hace aparecer o desaparecer niveles del edificio a lo largo de la película", FxCategory.Animation, "",
            [Toggle("LayersAnimationCheck", "Usar en la película", neutral: false)], Featured: true)
    ];

    public static FxEffect? Find(string id) => All.FirstOrDefault(effect => effect.Id == id);

    /// <summary>The effects the picker shows for a category; Featured gathers the most used ones from every group.</summary>
    public static IEnumerable<FxEffect> In(FxCategory category) =>
        All.Where(effect => category == FxCategory.Featured ? effect.Featured : effect.Category == category);
}

/// <summary>
/// A saved look: which effects are in the stack and the value of every setting they edit, so one click gives a scene the
/// same finish. Stored as a small JSON file the person can share.
/// </summary>
public sealed record FxStyle(string Name, List<string> Effects, Dictionary<string, double> Values)
{
    public const int CurrentVersion = 1;
    private const int MaximumEntries = 256;

    public string ToJson() => JsonSerializer.Serialize(new { Version = CurrentVersion, Name, Effects, Values },
        new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Reads a style file, or null when it is not one this version understands (wrong version, unknown effects, bad numbers).</summary>
    public static FxStyle? TryParse(string json)
    {
        // Styles are shareable files: validate their entire structure before the UI
        // resets any effect. The desktop also checks the on-disk byte count.
        if (json is null || json.Length > 256 * 1024) return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.GetProperty("Version").GetInt32() != CurrentVersion) return null;
            var name = root.GetProperty("Name").GetString()?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 120) return null;

            var effectsElement = root.GetProperty("Effects");
            var valuesElement = root.GetProperty("Values");
            if (effectsElement.ValueKind != JsonValueKind.Array || valuesElement.ValueKind != JsonValueKind.Object ||
                effectsElement.GetArrayLength() > MaximumEntries) return null;

            var effects = new List<string>();
            var allowedKeys = new HashSet<string>(StringComparer.Ordinal);
            var uniqueEffects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in effectsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) return null;
                var id = item.GetString()!;
                var effect = RenderFx.Find(id);
                // The UI only saves/applies active finishing effects; scene and
                // unimplemented effects must not masquerade as a loaded style.
                if (effect is null || effect.Planned || !effect.Removable || !uniqueEffects.Add(id)) return null;
                effects.Add(id);
                // A list pick is a position in this scene's list of materials, so a shared style never carries it.
                foreach (var control in effect.Controls.Where(item => item.Kind != FxControlKind.Choice)) allowedKeys.Add(control.Key);
            }

            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var entry in valuesElement.EnumerateObject())
            {
                if (values.Count >= MaximumEntries || !allowedKeys.Contains(entry.Name) ||
                    entry.Value.ValueKind != JsonValueKind.Number ||
                    !entry.Value.TryGetDouble(out var value) || !double.IsFinite(value) ||
                    !values.TryAdd(entry.Name, value)) return null;
            }
            return new FxStyle(name, effects, values);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

}
