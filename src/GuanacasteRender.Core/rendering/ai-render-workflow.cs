using System.Text;
using System.Text.Json;

namespace Guanacaste3D.Core.Rendering;

/// <summary>Styles available for an external image-generation service; no account or API is needed.</summary>
public enum AiRenderStyle
{
    Photorealistic,
    ArchitecturalModel,
    Interior,
    Night,
    Sunset,
    Watercolor,
    Concept
}

/// <summary>Selections are kept independent so a prompt expresses exactly what the user asked for.</summary>
public sealed record AiRenderOptions(
    AiRenderStyle Style,
    bool ImproveMaterials = true,
    bool ImproveLighting = true,
    bool ImproveShadows = true,
    bool AddTrees = false,
    bool AddVegetation = false,
    bool AddFurniture = false,
    bool AddPeople = false,
    bool ImproveSky = true,
    bool PreserveArchitecture = true,
    string? AdditionalInstructions = null);

/// <summary>Provider-neutral, Spanish Markdown instructions for image-to-image architectural rendering.</summary>
public static class AiRenderPromptBuilder
{
    public static string Generate(AiRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var style = options.Style switch
        {
            AiRenderStyle.Photorealistic => "Render arquitectónico fotorrealista de alta calidad, fotografía profesional, texturas físicamente plausibles.",
            AiRenderStyle.ArchitecturalModel => "Fotografía de una maqueta arquitectónica física a escala, materiales de maqueta, base de presentación y luz de estudio.",
            AiRenderStyle.Interior => "Visualización profesional de interiores, materiales y luz natural realistas, atmósfera habitable.",
            AiRenderStyle.Night => "Visualización arquitectónica nocturna, iluminación interior y exterior equilibrada y exposición fotográfica realista.",
            AiRenderStyle.Sunset => "Visualización arquitectónica al atardecer, luz cálida rasante y ambiente cinematográfico natural.",
            AiRenderStyle.Watercolor => "Ilustración arquitectónica en acuarela, papel texturizado, trazos artísticos y colores suaves.",
            AiRenderStyle.Concept => "Visualización conceptual arquitectónica de alta calidad, composición profesional y materiales estilizados.",
            _ => throw new ArgumentOutOfRangeException(nameof(options))
        };

        var prompt = new StringBuilder();
        prompt.AppendLine("# Render con IA — Guanacaste 3D");
        prompt.AppendLine();
        prompt.AppendLine("## Instrucción principal");
        prompt.AppendLine("Usa la imagen de referencia que adjunto como base para generar UNA nueva imagen arquitectónica.");
        prompt.AppendLine("La referencia determina el punto de vista, perspectiva, proporciones, emplazamiento y composición.");
        prompt.AppendLine("No diseñes una casa nueva: transforma visualmente la MISMA escena de la imagen.");
        using (var fidelityStream = typeof(AiRenderPromptBuilder).Assembly.GetManifestResourceStream("Guanacaste3D.AiSceneFidelity.es.txt")
            ?? throw new InvalidOperationException("The localized AI fidelity instructions are missing."))
        using (var fidelityReader = new StreamReader(fidelityStream, Encoding.UTF8))
            prompt.AppendLine(fidelityReader.ReadToEnd());
        prompt.AppendLine();
        prompt.AppendLine("## Estilo solicitado");
        prompt.AppendLine(style);
        prompt.AppendLine();
        prompt.AppendLine("## Ajustes solicitados");
        Add(prompt, options.ImproveMaterials, "Mejora los materiales: microtexturas, reflexión, rugosidad y detalles coherentes con cada superficie.");
        Add(prompt, options.ImproveLighting, "Mejora la iluminación: rebotes indirectos, exposición y luz ambiental físicamente coherentes.");
        Add(prompt, options.ImproveShadows, "Mejora las sombras proyectadas y de contacto, con bordes y direcciones consistentes con las fuentes de luz.");
        Add(prompt, options.AddTrees, "Incorpora árboles de aspecto natural a escala arquitectónica sin tapar los elementos importantes.");
        Add(prompt, options.AddVegetation, "Enriquece el paisajismo con vegetación realista, césped y pequeños detalles botánicos.");
        Add(prompt, options.AddFurniture, "Añade mobiliario apropiado a los espacios visibles y a la escala de la escena.");
        Add(prompt, options.AddPeople, "Incorpora pocas personas de escala correcta, anatomía realista, proporciones naturales y poses creíbles.");
        Add(prompt, options.ImproveSky, "Perfecciona cielo, nubes y atmósfera sin modificar la hora del día si no corresponde al estilo.");
        if (!options.AddPeople) Add(prompt, true, "No añadas personas que no aparezcan en la imagen de referencia.");
        if (!options.AddFurniture) Add(prompt, true, "No inventes muebles ni elementos decorativos nuevos.");
        if (!options.AddTrees && !options.AddVegetation) Add(prompt, true, "No agregues árboles ni vegetación que no estén ya presentes.");
        prompt.AppendLine();
        prompt.AppendLine("## Fidelidad geométrica");
        if (options.PreserveArchitecture)
        {
            prompt.AppendLine("Conserva estrictamente la estructura, geometría, distribución, puertas, ventanas, cubiertas y niveles de la imagen original.");
            prompt.AppendLine("No muevas, suprimas ni dupliques elementos arquitectónicos. Respeta el ángulo de cámara y evita deformaciones.");
        }
        else
        {
            prompt.AppendLine("Prioriza la composición de la imagen original, permitiendo ajustes visuales menores sin cambiar la identidad del proyecto.");
        }
        prompt.AppendLine("Evita objetos flotantes, texturas borrosas, proporciones imposibles, artefactos, rótulos, marcas de agua y texto añadido.");
        var notes = options.AdditionalInstructions?.Trim();
        if (!string.IsNullOrWhiteSpace(notes))
        {
            prompt.AppendLine();
            prompt.AppendLine("## Indicaciones adicionales del usuario");
            prompt.AppendLine(notes.Length > 2000 ? notes[..2000] : notes);
        }
        prompt.AppendLine();
        prompt.AppendLine("## Entrega");
        prompt.AppendLine("Devuelve la imagen final con la mejor resolución disponible y sin explicaciones impresas sobre la imagen.");
        prompt.AppendLine("Si la plataforma permite subir imágenes, usa el archivo referencia.png adjunto junto a este texto.");
        return prompt.ToString();
    }

    private static void Add(StringBuilder builder, bool enabled, string value)
    {
        if (enabled) builder.Append("- ").AppendLine(value);
    }
}

/// <summary>A single image-to-image attempt and its audit trail, all under one directory.</summary>
public sealed record AiRenderPackage(
    string DirectoryPath, string PromptPath, string ReferencePath, string Prompt,
    DateTimeOffset CreatedUtc, AiRenderStyle Style, int ResultCount)
{
    public string DisplayName => $"{CreatedUtc.ToLocalTime():dd/MM/yyyy HH:mm} · {Style} · {ResultCount} resultado(s)";
}

/// <summary>
/// Sidecar vault for saved projects (project.g3d.renders), with a local draft vault for projects not yet saved.
/// Reference, Markdown prompt and returned pictures always remain in the same attempt folder.
/// </summary>
public static class AiRenderVault
{
    private const long MaxImportedBytes = 100L * 1024 * 1024;
    private static readonly byte[] PngHeader = [137, 80, 78, 71, 13, 10, 26, 10];
    private sealed record Metadata(DateTimeOffset CreatedUtc, AiRenderStyle Style);

    public static string GetVaultDirectory(string? projectPath, string projectName)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
            return Path.GetFullPath(projectPath) + ".renders";

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
        return Path.Combine(local, "Guanacaste3D", "RenderBaul", "Borradores", SafeFileName(projectName));
    }

    public static AiRenderPackage CreateRequest(string? projectPath, string projectName,
        AiRenderOptions options, byte[] referencePng)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(referencePng);
        if (!HasPngHeader(referencePng))
            throw new InvalidDataException("La vista de referencia debe ser una imagen PNG válida.");

        var root = GetVaultDirectory(projectPath, projectName);
        Directory.CreateDirectory(root);
        var utc = DateTimeOffset.UtcNow;
        var folder = Path.Combine(root, utc.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var promptPath = Path.Combine(folder, "prompt.md");
            var referencePath = Path.Combine(folder, "referencia.png");
            var prompt = AiRenderPromptBuilder.Generate(options);
            File.WriteAllText(promptPath, prompt, new UTF8Encoding(false));
            File.WriteAllBytes(referencePath, referencePng);
            File.WriteAllText(Path.Combine(folder, "metadata.json"),
                JsonSerializer.Serialize(new Metadata(utc, options.Style)), new UTF8Encoding(false));
            return new AiRenderPackage(folder, promptPath, referencePath, prompt, utc, options.Style, 0);
        }
        catch
        {
            Directory.Delete(folder, recursive: true);
            throw;
        }
    }

    public static IReadOnlyList<AiRenderPackage> ListRequests(string? projectPath, string projectName)
    {
        var root = GetVaultDirectory(projectPath, projectName);
        if (!Directory.Exists(root)) return [];
        var packages = new List<AiRenderPackage>();
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            try
            {
                var promptPath = Path.Combine(folder, "prompt.md");
                var referencePath = Path.Combine(folder, "referencia.png");
                var metadataPath = Path.Combine(folder, "metadata.json");
                if (!File.Exists(promptPath) || !File.Exists(referencePath) || !File.Exists(metadataPath)) continue;
                var info = JsonSerializer.Deserialize<Metadata>(File.ReadAllText(metadataPath));
                if (info is null) continue;
                var count = Directory.EnumerateFiles(folder, "resultado-*", SearchOption.TopDirectoryOnly).Count();
                packages.Add(new AiRenderPackage(folder, promptPath, referencePath, File.ReadAllText(promptPath),
                    info.CreatedUtc, info.Style, count));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // An interrupted or manually modified request must not hide the rest of the vault.
            }
        }
        return packages.OrderByDescending(item => item.CreatedUtc).ToArray();
    }

    /// <summary>Copies the original external image into the selected attempt without altering other versions.</summary>
    public static string ImportResult(AiRenderPackage request, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        if (!File.Exists(request.PromptPath) || !File.Exists(request.ReferencePath))
            throw new InvalidDataException("La sesión seleccionada ya no contiene su prompt y referencia.");

        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg"))
            throw new InvalidDataException("Solo se admiten imágenes PNG o JPEG.");
        var file = new FileInfo(imagePath);
        if (file.Length == 0 || file.Length > MaxImportedBytes)
            throw new InvalidDataException("La imagen debe tener un tamaño entre 1 byte y 100 MB.");

        Span<byte> header = stackalloc byte[8];
        using (var stream = File.OpenRead(imagePath))
            stream.ReadExactly(header);
        if (extension == ".png" ? !header.SequenceEqual(PngHeader) : header[0] != 0xFF || header[1] != 0xD8)
            throw new InvalidDataException("El contenido de la imagen no coincide con el formato indicado.");

        var destination = Path.Combine(request.DirectoryPath,
            "resultado-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + extension);
        File.Copy(imagePath, destination, overwrite: false);
        return destination;
    }

    private static bool HasPngHeader(byte[] bytes) => bytes.Length >= PngHeader.Length &&
        bytes.AsSpan(0, PngHeader.Length).SequenceEqual(PngHeader);

    private static string SafeFileName(string name)
    {
        var safe = string.Concat((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "SinNombre" : safe;
    }
}
