using System.Runtime.InteropServices;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Writes a small SketchUp file through the same C API the reader uses, so the SketchUp import can be tested end to
/// end without shipping a binary fixture: a red box, a blue box inside a translated group, two green component
/// instances sharing one definition, and a hidden group that the importer must skip.
/// </summary>
internal sealed partial class SketchUpApi
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateModelFn(out nint model);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateHandleFn(out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FaceFn(out nint face, [In] Point3D[] vertices, nuint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AddHandlesFn(nint owner, nuint length, [In] nint[] handles);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AddOneFn(nint owner, nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AddInstanceFn(nint entities, nint instance, nint name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateInstanceFn(nint definition, out nint instance);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetColorFn(nint material, ref Color32 color);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetTransformFn(nint target, ref Transformation transformation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetHiddenFn(nint element, byte hidden);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SaveFn(nint model, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    /// <summary>Creates the sample file; returns false when the SketchUp library is not installed.</summary>
    public static bool TryWriteSample(string path)
    {
        using var api = Open();
        if (api is null) return false;
        api.WriteSample(path);
        return true;
    }

    private nint Material(nint model, byte red, byte green, byte blue)
    {
        Check(Function<CreateHandleFn>("SUMaterialCreate")(out var material), "material");
        var color = new Color32 { Red = red, Green = green, Blue = blue, Alpha = 255 };
        Check(Function<SetColorFn>("SUMaterialSetColor")(material, ref color), "color");
        Check(Function<AddHandlesFn>("SUModelAddMaterials")(model, 1, [material]), "materials");
        return material;
    }

    /// <summary>Adds an axis-aligned box (inches) as six faces painted with <paramref name="material"/>.</summary>
    private void AddBox(nint entities, nint material, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        Point3D P(double x, double y, double z) => new() { X = x, Y = y, Z = z };
        // Each face is listed counter-clockwise as seen from outside.
        Point3D[][] quads =
        [
            [P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0), P(x1, y0, z0)], // bottom
            [P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1)], // top
            [P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1)], // front
            [P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1)], // right
            [P(x1, y1, z0), P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1)], // back
            [P(x0, y1, z0), P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1)]  // left
        ];
        var faces = new nint[quads.Length];
        for (var i = 0; i < quads.Length; i++)
        {
            Check(Function<FaceFn>("SUFaceCreateSimple")(out faces[i], quads[i], 4), "face");
            Check(Function<AddOneFn>("SUFaceSetFrontMaterial")(faces[i], material), "face material");
        }
        Check(Function<AddHandlesFn>("SUEntitiesAddFaces")(entities, (nuint)faces.Length, faces), "add faces");
    }

    private static Transformation Translation(double x, double y, double z)
    {
        var t = new Transformation();
        t.Values[0] = t.Values[5] = t.Values[10] = t.Values[15] = 1;
        t.Values[12] = x;
        t.Values[13] = y;
        t.Values[14] = z;
        return t;
    }

    private void WriteSample(string path)
    {
        Check(Function<CreateModelFn>("SUModelCreate")(out var model), "model");
        try
        {
            var entities = ModelEntities(model);
            var red = Material(model, 220, 40, 40);
            var blue = Material(model, 40, 60, 220);
            var green = Material(model, 40, 200, 70);
            AddBox(entities, red, 0, 0, 0, 100, 100, 100);

            Check(Function<CreateHandleFn>("SUGroupCreate")(out var group), "group");
            Check(Function<AddOneFn>("SUEntitiesAddGroup")(entities, group), "add group");
            AddBox(GroupEntities(group), blue, 0, 0, 0, 40, 40, 40);
            var move = Translation(200, 0, 0);
            Check(Function<SetTransformFn>("SUGroupSetTransform")(group, ref move), "group transform");

            Check(Function<CreateHandleFn>("SUComponentDefinitionCreate")(out var definition), "definition");
            Check(Function<AddHandlesFn>("SUModelAddComponentDefinitions")(model, 1, [definition]), "definitions");
            Check(Function<GetEntitiesFn>("SUComponentDefinitionGetEntities")(definition, out var definitionEntities), "definition entities");
            AddBox(definitionEntities, green, 0, 0, 0, 20, 20, 20);
            foreach (var x in new[] { 0.0, 100.0 })
            {
                Check(Function<CreateInstanceFn>("SUComponentDefinitionCreateInstance")(definition, out var instance), "instance");
                Check(Function<AddInstanceFn>("SUEntitiesAddInstance")(entities, instance, 0), "add instance");
                var place = Translation(x, 200, 0);
                Check(Function<SetTransformFn>("SUComponentInstanceSetTransform")(instance, ref place), "instance transform");
            }

            Check(Function<CreateHandleFn>("SUGroupCreate")(out var hidden), "hidden group");
            Check(Function<AddOneFn>("SUEntitiesAddGroup")(entities, hidden), "add hidden");
            AddBox(GroupEntities(hidden), red, 0, 0, 0, 10, 10, 10);
            Check(Function<SetHiddenFn>("SUDrawingElementSetHidden")(GroupElement(hidden), 1), "hide");

            Check(Function<SaveFn>("SUModelSaveToFile")(model, path), "save");
        }
        finally
        {
            ReleaseModel(model);
        }
    }
}
