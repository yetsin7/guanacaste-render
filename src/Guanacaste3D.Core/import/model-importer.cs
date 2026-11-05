using System.Diagnostics;
using System.Text;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Entry point for bringing external geometry into Guanacaste 3D. Formats are recognized by their first bytes,
/// so a renamed file is still handled correctly. Open formats (OBJ, DXF, STL, IFC) are read directly; DWG is
/// converted through AutoCAD or the ODA File Converter when installed; SketchUp uses the SketchUp SDK library when
/// present; Revit files yield their metadata and preview only, because their geometry is a proprietary database.
/// </summary>
public static class ModelImporter
{
    private const long MaximumFileBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>File-dialog filter covering every format that can be inspected.</summary>
    public const string DialogFilter =
        "Modelos 3D y CAD|*.obj;*.dxf;*.dwg;*.stl;*.ifc;*.skp;*.rvt;*.g3dx;*.rfa;*.rte;*.rft;*.dae;*.glb;*.gltf;*.g3d|" +
        "Wavefront OBJ (*.obj)|*.obj|AutoCAD DXF (*.dxf)|*.dxf|AutoCAD DWG (*.dwg)|*.dwg|" +
        "IFC (*.ifc)|*.ifc|SketchUp (*.skp)|*.skp|Revit (*.rvt;*.rfa)|*.rvt;*.rfa;*.rte;*.rft|Intercambio Revit (*.g3dx)|*.g3dx|" +
        "STL (*.stl)|*.stl|COLLADA (*.dae)|*.dae|glTF (*.glb;*.gltf)|*.glb;*.gltf|Proyecto Guanacaste 3D (*.g3d)|*.g3d";

    /// <summary>Recognizes the format from the file's first bytes, falling back to its extension.</summary>
    public static ImportFormat Detect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException("The model file does not exist.", path);
        Span<byte> head = stackalloc byte[512];
        int read;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            read = stream.Read(head);
        head = head[..read];
        if (read >= 8 && head[..8].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }))
            return ImportFormat.Revit;
        if (read >= 6 && head[0] == 'A' && head[1] == 'C' && head[2] == '1' && head[3] == '0') return ImportFormat.Dwg;
        if (read >= 4 && head[0] == 0xFF && head[1] == 0xFE && head[2] == 0xFF && head[3] == 0x0E) return ImportFormat.Skp;
        if (read >= 4 && head[0] == 'g' && head[1] == 'l' && head[2] == 'T' && head[3] == 'F') return ImportFormat.Gltf;
        var text = Encoding.ASCII.GetString(head);
        if (text.StartsWith("ISO-10303-21", StringComparison.Ordinal)) return ImportFormat.Ifc;
        if (text.Contains("AutoCAD Binary DXF", StringComparison.Ordinal)) return ImportFormat.Dxf;
        if (text.Contains("COLLADA", StringComparison.OrdinalIgnoreCase)) return ImportFormat.Collada;

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".obj" => ImportFormat.Obj,
            ".dxf" => ImportFormat.Dxf,
            ".dwg" => ImportFormat.Dwg,
            ".stl" => ImportFormat.Stl,
            ".ifc" => ImportFormat.Ifc,
            ".skp" => ImportFormat.Skp,
            ".rvt" or ".rfa" or ".rte" or ".rft" => ImportFormat.Revit,
            ".glb" or ".gltf" => ImportFormat.Gltf,
            ".dae" => ImportFormat.Collada,
            ".g3d" => ImportFormat.Native,
            _ => LooksLikeDxf(text) ? ImportFormat.Dxf : ImportFormat.Unknown
        };
    }

    private static bool LooksLikeDxf(string text) =>
        text.TrimStart().StartsWith("0\r\nSECTION", StringComparison.Ordinal) ||
        text.TrimStart().StartsWith("0\nSECTION", StringComparison.Ordinal);

    /// <summary>
    /// Describes a file without reading its geometry: the format, whether it can be imported on this computer, and
    /// what the user should know (for Revit, the version and preview image).
    /// </summary>
    public static ImportInspection Inspect(string path)
    {
        var format = Detect(path);
        var bytes = new FileInfo(path).Length;
        var notes = new List<string>();
        switch (format)
        {
            case ImportFormat.Revit:
                return RevitFileInfo.Inspect(path, bytes);
            case ImportFormat.Skp:
                return SketchUpReader.Inspect(path, bytes);
            case ImportFormat.Dwg:
                return DwgConverter.Inspect(path, bytes);
            case ImportFormat.Unknown:
                return new ImportInspection(format, "?", bytes, false, "Import.Summary.Unknown", notes);
            default:
                var name = format switch
                {
                    ImportFormat.Obj => "Wavefront OBJ",
                    ImportFormat.Dxf => "AutoCAD DXF",
                    ImportFormat.Stl => "STL",
                    ImportFormat.Ifc => "IFC",
                    ImportFormat.Gltf => "glTF",
                    ImportFormat.Native => "Guanacaste 3D",
                    _ => "COLLADA"
                };
                if (format == ImportFormat.Dxf && DxfReader.IsBinary(path))
                    return new ImportInspection(format, name, bytes, false, "Import.Summary.BinaryDxf", notes);
                if (bytes > 1_500_000_000) notes.Add("Import.Note.VeryLarge");
                return new ImportInspection(format, name, bytes, true, "Import.Summary.Ready", notes);
        }
    }

    /// <summary>Reads a file on a worker thread, reporting progress and honoring cancellation.</summary>
    public static Task<ImportResult> ImportAsync(string path, ModelImportOptions? options = null,
        IProgress<ImportProgress>? progress = null, CancellationToken cancellation = default) =>
        Task.Run(() => Import(path, options ?? new ModelImportOptions(), progress, cancellation), cancellation);

    /// <summary>Reads a file synchronously.</summary>
    public static ImportResult Import(string path, ModelImportOptions? options = null,
        IProgress<ImportProgress>? progress = null, CancellationToken cancellation = default)
    {
        options ??= new ModelImportOptions();
        var clock = Stopwatch.StartNew();
        if (!File.Exists(path)) throw new InvalidDataException("The model file does not exist.");
        var size = new FileInfo(path).Length;
        if (size == 0) throw new InvalidDataException("The model file is empty.");
        if (size > MaximumFileBytes) throw new InvalidDataException("The model file is too large to import.");

        var format = Detect(path);
        var model = format switch
        {
            ImportFormat.Obj => ObjReader.Read(path, options, progress, cancellation),
            ImportFormat.Dxf => DxfReader.Read(path, options, progress, cancellation),
            ImportFormat.Stl => StlReader.Read(path, options, progress, cancellation),
            ImportFormat.Ifc => IfcReader.Read(path, options, progress, cancellation),
            ImportFormat.Skp => SketchUpReader.Read(path, options, progress, cancellation),
            ImportFormat.Dwg => DwgConverter.Read(path, options, progress, cancellation),
            ImportFormat.Gltf => GltfReader.Read(path, options, progress, cancellation),
            ImportFormat.Collada => ColladaReader.Read(path, options, progress, cancellation),
            ImportFormat.Native => NativeProjectReader.Read(path, options, progress, cancellation),
            ImportFormat.Revit => throw new InvalidDataException(
                "Revit keeps its geometry in a proprietary format; export the model to IFC from Revit and import that file."),
            _ => throw new InvalidDataException("The model file format is not supported.")
        };
        if (!model.HasGeometry) throw new InvalidDataException("The model file contains no geometry.");
        progress?.Report(new ImportProgress(1, "Import.Done"));
        Rendering.MemoryTrim.AfterHeavyBuild();
        return new ImportResult(model, format, clock.Elapsed);
    }
}
