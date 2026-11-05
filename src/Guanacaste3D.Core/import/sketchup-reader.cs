using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Imports SketchUp (.skp) files through the SketchUp C API, loaded at run time from a library already present on
/// the computer (the one Autodesk installs with Revit/AutoCAD, or SketchUp's own). Nothing is bundled, so the
/// application builds and runs without it and reports a clear message when it is missing. Groups and components
/// are flattened with their transforms; hidden objects and hidden tags are skipped; face colors become materials.
/// </summary>
internal static partial class SketchUpReader
{
    private const int MaximumDepth = 24;

    // ------------------------------------------------------------------ inspection

    public static ImportInspection Inspect(string path, long bytes)
    {
        var notes = new List<string>();
        var version = ReadHeaderVersion(path);
        if (version is not null) notes.Add("Import.Note.SketchUpVersion|" + version);
        var library = SketchUpApi.FindLibrary();
        if (library is null)
        {
            notes.Add("Import.Note.SketchUpSdkMissing");
            return new ImportInspection(ImportFormat.Skp, "SketchUp", bytes, false, "Import.Summary.SketchUpMissing", notes);
        }
        notes.Add("Import.Note.SketchUpSdk|" + library);
        return new ImportInspection(ImportFormat.Skp, "SketchUp", bytes, true, "Import.Summary.Ready", notes);
    }

    /// <summary>Reads the "{major.minor.build}" version string from the file header; null when absent.</summary>
    private static string? ReadHeaderVersion(string path)
    {
        var head = new byte[160];
        int read;
        using (var stream = File.OpenRead(path)) read = stream.Read(head, 0, head.Length);
        var text = Encoding.Unicode.GetString(head, 0, read & ~1);
        var match = VersionPattern().Match(text);
        if (!match.Success) return null;
        // SketchUp numbers releases by year since 2014 ("{21.1.299}" is SketchUp 2021).
        return int.TryParse(match.Groups[1].Value, out var major) && major is >= 14 and <= 40
            ? $"20{major:00}"
            : match.Value.Trim('{', '}');
    }

    [GeneratedRegex(@"\{(\d+)\.(\d+)\.(\d+)\}")]
    private static partial Regex VersionPattern();

    // ------------------------------------------------------------------ import

    public static ImportedModel Read(string path, ModelImportOptions options, IProgress<ImportProgress>? progress,
        CancellationToken cancellation)
    {
        using var api = SketchUpApi.Open() ??
            throw new InvalidDataException("SketchUp files need the SketchUp SDK library, which was not found on this computer.");
        progress?.Report(new ImportProgress(0.05, "Import.Reading"));
        var model = api.OpenModel(path);
        try
        {
            var mesh = new MeshAccumulator { Normals = new ArrayBuilder<Vector3>(1 << 14) };
            var context = new Context(api, mesh, options, progress, cancellation);
            context.Collect(api.ModelEntities(model), Matrix4x4.Identity, 0);
            if (mesh.TriangleCount == 0) throw new InvalidDataException("The model file contains no geometry.");
            if (context.SkippedHidden > 0) mesh.Warn("Import.Warning.HiddenSkipped");
            // SketchUp always stores inches regardless of the units shown in its interface.
            var scale = options.MetersPerUnit ?? 0.0254;
            return mesh.Build(path, "SKP", ModelFrame.ZUp, scale, options);
        }
        finally
        {
            api.ReleaseModel(model);
        }
    }

    private sealed class Context(SketchUpApi api, MeshAccumulator mesh, ModelImportOptions options,
        IProgress<ImportProgress>? progress, CancellationToken cancellation)
    {
        private readonly Dictionary<nint, int> _materials = [];
        private readonly Dictionary<nint, bool> _layerVisible = [];
        private int _faces;

        public int SkippedHidden { get; private set; }

        public void Collect(nint entities, Matrix4x4 transform, int depth)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var face in api.Faces(entities))
            {
                if (api.IsHidden(api.FaceElement(face), LayerVisible)) { SkippedHidden++; continue; }
                AddFace(face, transform);
                if ((++_faces & 0x3FF) == 0)
                    progress?.Report(new ImportProgress(Math.Min(0.9, 0.1 + _faces / 2_000_000.0), "Import.Reading"));
            }
            if (depth >= MaximumDepth) return;
            foreach (var group in api.Groups(entities))
            {
                if (api.IsHidden(api.GroupElement(group), LayerVisible)) { SkippedHidden++; continue; }
                Collect(api.GroupEntities(group), api.GroupTransform(group) * transform, depth + 1);
            }
            foreach (var instance in api.Instances(entities))
            {
                if (api.IsHidden(api.InstanceElement(instance), LayerVisible)) { SkippedHidden++; continue; }
                var definition = api.InstanceDefinitionEntities(instance);
                if (definition != 0) Collect(definition, api.InstanceTransform(instance) * transform, depth + 1);
            }
        }

        private bool LayerVisible(nint layer)
        {
            if (!_layerVisible.TryGetValue(layer, out var visible))
                _layerVisible[layer] = visible = api.LayerVisible(layer);
            return visible;
        }

        private void AddFace(nint face, Matrix4x4 transform)
        {
            var (positions, normals, indices) = api.Triangulate(face);
            if (indices.Length == 0) return;
            if (_faces > 4_000_000 || mesh.TriangleCount + indices.Length / 3 > options.MaximumTriangles)
                throw new InvalidDataException("The model file is too large to import.");
            var material = ResolveMaterial(face);
            var start = mesh.Positions.Count;
            var mirrored = transform.GetDeterminant() < 0;
            for (var i = 0; i < positions.Length; i++)
            {
                mesh.Positions.Add(Vector3.Transform(positions[i], transform));
                var normal = Vector3.TransformNormal(normals[i], transform);
                mesh.Normals!.Add(normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.Zero);
            }
            for (var i = 0; i + 2 < indices.Length; i += 3)
                mesh.AddTriangle(start + indices[i], start + indices[mirrored ? i + 2 : i + 1],
                    start + indices[mirrored ? i + 1 : i + 2], material);
        }

        private int ResolveMaterial(nint face)
        {
            var handle = api.FrontMaterial(face);
            if (handle == 0)
                return mesh.MaterialIndex("skp:default", () => MaterialLibrary.Imported with { Name = "SketchUp" });
            if (_materials.TryGetValue(handle, out var existing)) return existing;
            var (red, green, blue, opacity) = api.MaterialColor(handle);
            var color = MaterialLibrary.LinearFromSrgb(red, green, blue);
            var index = mesh.MaterialIndex($"skp:{handle}", () => opacity < 0.97
                ? MaterialLibrary.ClearGlass with
                {
                    Name = "Vidrio", Transmission = (float)Math.Clamp(1 - opacity, 0.2, 0.95),
                    TransmissionTint = Vector3.Lerp(new Vector3(0.9f, 0.95f, 0.97f), color, 0.3f)
                }
                : new RenderMaterial { Name = "SketchUp", BaseColor = color, Roughness = 0.8f });
            _materials[handle] = index;
            return index;
        }
    }
}

/// <summary>Run-time binding to the SketchUp C API (SketchUpAPI.dll).</summary>
internal sealed partial class SketchUpApi : IDisposable
{
    private const int Success = 0;
    private static readonly object InitializeLock = new();
    private static int _initializations;

    private readonly nint _library;
    private readonly Dictionary<string, Delegate> _functions = [];

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point3D { public double X, Y, Z; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Color32 { public byte Red, Green, Blue, Alpha; }

    /// <summary>Sixteen doubles laid out inline, which marshals as a plain C array without unsafe code.</summary>
    [System.Runtime.CompilerServices.InlineArray(16)]
    internal struct Sixteen { private double _first; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Transformation { public Sixteen Values; }

    private SketchUpApi(nint library) => _library = library;

    // ---- discovery and loading

    /// <summary>Returns the path of a SketchUp API library on this computer, or null.</summary>
    public static string? FindLibrary()
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("GUANACASTE_SKETCHUP_API") is { Length: > 0 } custom) candidates.Add(custom);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "SketchUpAPI.dll"));
        foreach (var root in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "Autodesk Shared", "Components"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SketchUp")
                 })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                // Newest library first: higher release folders read more recent files.
                candidates.AddRange(Directory.EnumerateFiles(root, "SketchUpAPI.dll", SearchOption.AllDirectories)
                    .OrderByDescending(file => file, StringComparer.OrdinalIgnoreCase).Take(8));
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Loads and initializes the library, or returns null when it is not installed or cannot be loaded.</summary>
    public static SketchUpApi? Open()
    {
        var path = FindLibrary();
        if (path is null || !NativeLibrary.TryLoad(path, out var handle)) return null;
        var api = new SketchUpApi(handle);
        try
        {
            lock (InitializeLock)
            {
                // SUInitialize and SUTerminate return nothing; reading a status from them would read garbage.
                if (_initializations == 0) api.Function<InitializeFn>("SUInitialize")();
                _initializations++;
            }
            return api;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or InvalidDataException or DllNotFoundException)
        {
            NativeLibrary.Free(handle);
            return null;
        }
    }

    public void Dispose()
    {
        lock (InitializeLock)
        {
            if (--_initializations == 0) Function<TerminateFn>("SUTerminate")();
        }
        NativeLibrary.Free(_library);
    }

    // ---- delegates (the C API is cdecl; on x64 Windows there is a single convention)

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void InitializeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TerminateFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateFromFileFn(out nint model, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReleaseModelFn(ref nint model);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetEntitiesFn(nint owner, out nint entities);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetCountFn(nint entities, out nuint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetHandlesFn(nint entities, nuint length, [Out] nint[] handles, out nuint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ElementFn(nint source);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetHiddenFn(nint element, out byte hidden);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetLayerFn(nint element, out nint layer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetVisibleFn(nint layer, out byte visible);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetTransformFn(nint source, out Transformation transformation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDefinitionFn(nint instance, out nint definition);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MeshCreateFn(out nint helper, nint face);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MeshReleaseFn(ref nint helper);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MeshPointsFn(nint helper, nuint length, [Out] Point3D[] points, out nuint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MeshIndicesFn(nint helper, nuint length, [Out] nuint[] indices, out nuint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetMaterialFn(nint face, out nint material);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetColorFn(nint material, out Color32 color);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDoubleFn(nint material, out double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetBoolFn(nint material, out byte value);

    private T Function<T>(string name) where T : Delegate
    {
        if (_functions.TryGetValue(name, out var cached)) return (T)cached;
        var address = NativeLibrary.GetExport(_library, name);
        var created = Marshal.GetDelegateForFunctionPointer<T>(address);
        _functions[name] = created;
        return created;
    }

    private void Check(int result, string call)
    {
        if (result != Success) throw new InvalidDataException("The SketchUp file could not be read.");
    }

    // ---- model

    public nint OpenModel(string path)
    {
        var result = Function<CreateFromFileFn>("SUModelCreateFromFile")(out var model, path);
        // 8 = serialization error, 11 = file newer than the library, 14 = invalid input: all mean "cannot read this file".
        if (result != Success) throw new InvalidDataException("The SketchUp file could not be read.");
        return model;
    }

    public void ReleaseModel(nint model) => Function<ReleaseModelFn>("SUModelRelease")(ref model);

    public nint ModelEntities(nint model)
    {
        Check(Function<GetEntitiesFn>("SUModelGetEntities")(model, out var entities), "entities");
        return entities;
    }

    private IEnumerable<nint> Handles(nint entities, string countFunction, string listFunction)
    {
        if (Function<GetCountFn>(countFunction)(entities, out var count) != Success || count == 0) return [];
        var handles = new nint[(int)count];
        return Function<GetHandlesFn>(listFunction)(entities, count, handles, out var actual) == Success
            ? handles.Take((int)actual)
            : [];
    }

    public IEnumerable<nint> Faces(nint entities) => Handles(entities, "SUEntitiesGetNumFaces", "SUEntitiesGetFaces");
    public IEnumerable<nint> Groups(nint entities) => Handles(entities, "SUEntitiesGetNumGroups", "SUEntitiesGetGroups");
    public IEnumerable<nint> Instances(nint entities) => Handles(entities, "SUEntitiesGetNumInstances", "SUEntitiesGetInstances");

    public nint GroupEntities(nint group)
    {
        Check(Function<GetEntitiesFn>("SUGroupGetEntities")(group, out var entities), "group");
        return entities;
    }

    public nint InstanceDefinitionEntities(nint instance)
    {
        if (Function<GetDefinitionFn>("SUComponentInstanceGetDefinition")(instance, out var definition) != Success) return 0;
        return Function<GetEntitiesFn>("SUComponentDefinitionGetEntities")(definition, out var entities) == Success ? entities : 0;
    }

    public nint FaceElement(nint face) => Function<ElementFn>("SUFaceToDrawingElement")(face);
    public nint GroupElement(nint group) => Function<ElementFn>("SUGroupToDrawingElement")(group);
    public nint InstanceElement(nint instance) => Function<ElementFn>("SUComponentInstanceToDrawingElement")(instance);

    /// <summary>True when the element, or the tag it is on, is hidden.</summary>
    public bool IsHidden(nint element, Func<nint, bool> layerVisible)
    {
        if (element == 0) return false;
        if (Function<GetHiddenFn>("SUDrawingElementGetHidden")(element, out var hidden) == Success && hidden != 0) return true;
        return Function<GetLayerFn>("SUDrawingElementGetLayer")(element, out var layer) == Success && layer != 0 &&
               !layerVisible(layer);
    }

    public bool LayerVisible(nint layer) =>
        Function<GetVisibleFn>("SULayerGetVisibility")(layer, out var visible) != Success || visible != 0;

    public Matrix4x4 GroupTransform(nint group) => ToMatrix(Function<GetTransformFn>("SUGroupGetTransform"), group);
    public Matrix4x4 InstanceTransform(nint instance) => ToMatrix(Function<GetTransformFn>("SUComponentInstanceGetTransform"), instance);

    /// <summary>
    /// SketchUp stores 4×4 matrices column by column, which is exactly the row-vector layout .NET expects. The last
    /// element is a homogeneous divisor (an overall scale), folded into the other elements.
    /// </summary>
    private static Matrix4x4 ToMatrix(GetTransformFn function, nint source)
    {
        if (function(source, out var t) != Success) return Matrix4x4.Identity;
        var v = t.Values;
        var divisor = v[15] is 0 or double.NaN ? 1 : v[15];
        float E(int index) => (float)(v[index] / divisor);
        return new Matrix4x4(E(0), E(1), E(2), 0, E(4), E(5), E(6), 0, E(8), E(9), E(10), 0, E(12), E(13), E(14), 1);
    }

    /// <summary>Triangulates a face (holes included) into unique vertices with smooth normals.</summary>
    public (Vector3[] Positions, Vector3[] Normals, int[] Indices) Triangulate(nint face)
    {
        if (Function<MeshCreateFn>("SUMeshHelperCreate")(out var helper, face) != Success) return ([], [], []);
        try
        {
            Check(Function<GetCountFn>("SUMeshHelperGetNumVertices")(helper, out var vertexCount), "mesh");
            Check(Function<GetCountFn>("SUMeshHelperGetNumTriangles")(helper, out var triangleCount), "mesh");
            if (vertexCount == 0 || triangleCount == 0) return ([], [], []);
            var points = new Point3D[(int)vertexCount];
            var normals = new Point3D[(int)vertexCount];
            var indices = new nuint[(int)triangleCount * 3];
            Check(Function<MeshPointsFn>("SUMeshHelperGetVertices")(helper, vertexCount, points, out _), "vertices");
            Check(Function<MeshPointsFn>("SUMeshHelperGetNormals")(helper, vertexCount, normals, out _), "normals");
            Check(Function<MeshIndicesFn>("SUMeshHelperGetVertexIndices")(helper, (nuint)indices.Length, indices, out _), "indices");
            return (points.Select(p => new Vector3((float)p.X, (float)p.Y, (float)p.Z)).ToArray(),
                normals.Select(n => new Vector3((float)n.X, (float)n.Y, (float)n.Z)).ToArray(),
                indices.Select(i => (int)i).ToArray());
        }
        finally
        {
            Function<MeshReleaseFn>("SUMeshHelperRelease")(ref helper);
        }
    }

    public nint FrontMaterial(nint face) =>
        Function<GetMaterialFn>("SUFaceGetFrontMaterial")(face, out var material) == Success ? material : 0;

    /// <summary>Returns the material's sRGB color and opacity (1 = opaque).</summary>
    public (byte Red, byte Green, byte Blue, double Opacity) MaterialColor(nint material)
    {
        if (Function<GetColorFn>("SUMaterialGetColor")(material, out var color) != Success) return (180, 180, 180, 1);
        var opacity = 1.0;
        if (Function<GetBoolFn>("SUMaterialGetUseOpacity")(material, out var useOpacity) == Success && useOpacity != 0)
            Function<GetDoubleFn>("SUMaterialGetOpacity")(material, out opacity);
        return (color.Red, color.Green, color.Blue, opacity);
    }
}
