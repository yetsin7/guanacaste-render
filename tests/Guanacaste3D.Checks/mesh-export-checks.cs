using System.Text;
using System.Text.Json;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Interop;

/// <summary>OBJ/MTL and GLB export checks against the published file-format rules.</summary>
static class MeshExportChecks
{
    public static void Run(Action<bool, string> check)
    {
        var mesh = BuildingMesher.Build(SampleProjects.CreateDemonstrationHouse());

        var (obj, mtl) = MeshExporter.ToObj(mesh, "casa.mtl");
        var objLines = obj.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var vertexCount = objLines.Count(line => line.StartsWith("v ", StringComparison.Ordinal));
        var faces = objLines.Where(line => line.StartsWith("f ", StringComparison.Ordinal)).ToArray();
        var indicesInRange = faces.SelectMany(face => face.Split(' ').Skip(1)).Select(int.Parse)
            .All(index => index >= 1 && index <= vertexCount);
        check(vertexCount == mesh.Positions.Count && faces.Length == mesh.TriangleCount && indicesInRange,
            "OBJ export writes every vertex and triangle with 1-based indices in range");
        var used = objLines.Where(line => line.StartsWith("usemtl ", StringComparison.Ordinal)).Select(line => line[7..]).ToHashSet();
        var defined = mtl.Split('\n').Where(line => line.StartsWith("newmtl ", StringComparison.Ordinal)).Select(line => line[7..]).ToHashSet();
        check(used.SetEquals(defined) && used.Contains("G3D_Glass") && !obj.Contains(','),
            "OBJ materials are all defined in the MTL and numbers are culture invariant");

        var glb = MeshExporter.ToGlb(mesh, "Casa");
        check(BitConverter.ToUInt32(glb, 0) == 0x46546C67 && BitConverter.ToUInt32(glb, 4) == 2 &&
              BitConverter.ToUInt32(glb, 8) == glb.Length && glb.Length % 4 == 0,
            "GLB header has magic, version 2 and the exact total length");
        var jsonLength = (int)BitConverter.ToUInt32(glb, 12);
        var binOffset = 20 + jsonLength;
        var binLength = (int)BitConverter.ToUInt32(glb, binOffset);
        check(BitConverter.ToUInt32(glb, 16) == 0x4E4F534A && jsonLength % 4 == 0 &&
              BitConverter.ToUInt32(glb, binOffset + 4) == 0x004E4942 && binOffset + 8 + binLength == glb.Length,
            "GLB has 4-byte aligned JSON and BIN chunks");

        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength).TrimEnd());
        var root = json.RootElement;
        var accessors = root.GetProperty("accessors").EnumerateArray().ToArray();
        var views = root.GetProperty("bufferViews").EnumerateArray().ToArray();
        var viewsFit = views.All(view => view.GetProperty("byteOffset").GetInt32() % 4 == 0 &&
            view.GetProperty("byteOffset").GetInt32() + view.GetProperty("byteLength").GetInt32() <= binLength);
        var countsMatch = accessors.All(accessor =>
            accessor.GetProperty("count").GetInt32() * 12 ==
            views[accessor.GetProperty("bufferView").GetInt32()].GetProperty("byteLength").GetInt32());
        var primitives = root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray().ToArray();
        var positionsAccessors = primitives.Select(p => p.GetProperty("attributes").GetProperty("POSITION").GetInt32()).ToArray();
        var totalVertices = positionsAccessors.Sum(index => accessors[index].GetProperty("count").GetInt32());
        check(root.GetProperty("asset").GetProperty("version").GetString() == "2.0" && viewsFit && countsMatch &&
              positionsAccessors.All(index => accessors[index].TryGetProperty("min", out _)) &&
              totalVertices == mesh.TriangleCount * 3,
            "GLB accessors, buffer views and POSITION bounds are consistent with the mesh");

        // The first position must be the model's first triangle corner converted to Y-up.
        var firstView = views[accessors[positionsAccessors[0]].GetProperty("bufferView").GetInt32()];
        var firstOffset = binOffset + 8 + firstView.GetProperty("byteOffset").GetInt32();
        var firstKind = mesh.TriangleKinds.Min();
        var firstTriangle = Enumerable.Range(0, mesh.TriangleCount).First(t => mesh.TriangleKinds[t] == firstKind);
        var expected = mesh.Positions[mesh.Indices[firstTriangle * 3]];
        check(Math.Abs(BitConverter.ToSingle(glb, firstOffset) - expected.X) < 1e-6 &&
              Math.Abs(BitConverter.ToSingle(glb, firstOffset + 4) - expected.Z) < 1e-6 &&
              Math.Abs(BitConverter.ToSingle(glb, firstOffset + 8) + expected.Y) < 1e-6,
            "GLB stores model coordinates as Y-up meters");

        var emptyGlb = MeshExporter.ToGlb(new SceneMesh(), "Empty");
        check(BitConverter.ToUInt32(emptyGlb, 8) == emptyGlb.Length, "GLB export of an empty model stays valid");
    }
}
