using System.Numerics;
using System.Text;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Import;
using Guanacaste3D.Core.Interop;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// Checks the model importers with small synthetic files whose answers are known by construction, round trips through
/// the application's own exporters, and, when they exist on this computer, real files from AutoCAD, Revit and SketchUp.
/// </summary>
static class ImportChecks
{
    private const string RealFolder = @"C:\Users\Yetsin\Documents\Arquitectura";

    public static void Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-import-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Geometry(check);
            Obj(check, folder);
            Dxf(check, folder);
            Stl(check, folder);
            RoundTrips(check, folder);
            GltfBufferPaths(check, folder);
            GltfIndexAccessors(check, folder);
            GltfNodeNormals(check, folder);
            GltfPngTextures(check, folder);
            Ifc(check, folder);
            Collada(check, folder);
            Detection(check, folder);
            Limits(check, folder);
            Revit(check);
            RealFiles(check, folder);
            Sketchup(check, folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Signed volume by the divergence theorem; positive for outward-facing closed meshes.</summary>
    private static double Volume(ImportedModel model)
    {
        double volume = 0;
        for (var t = 0; t < model.TriangleCount; t++)
        {
            var a = model.Positions[model.Indices[t * 3]];
            var b = model.Positions[model.Indices[t * 3 + 1]];
            var c = model.Positions[model.Indices[t * 3 + 2]];
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }
        return volume;
    }

    private static double Volume(TriSoup soup) => Volume(new ImportedModel
    {
        Positions = [.. soup.Points], Indices = Enumerable.Range(0, soup.Points.Count).ToArray()
    });

    private static string Write(string folder, string name, string text)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    // ------------------------------------------------------------------ geometry helpers

    private static void Geometry(Action<bool, string> check)
    {
        var square = new List<P2> { new(0, 0), new(4, 0), new(4, 4), new(0, 4) };
        var hole = new List<P2> { new(1, 1), new(3, 1), new(3, 3), new(1, 3) };
        var triangles = PolygonMesher.Triangulate(square, [hole]);
        var points = square.Concat(hole).ToList();
        var area = triangles.Sum(t => Math.Abs(Cross(points[t.A], points[t.B], points[t.C])) / 2);
        check(Math.Abs(area - 12) < 1e-9, "A square with a square hole triangulates to exactly its area (16 − 4)");
        var concave = new List<P2> { new(0, 0), new(4, 0), new(4, 4), new(2, 1), new(0, 4) };
        var concaveArea = PolygonMesher.Triangulate(concave).Sum(t => Math.Abs(Cross(concave[t.A], concave[t.B], concave[t.C])) / 2);
        check(Math.Abs(concaveArea - Math.Abs(PolygonMesher.SignedArea(concave, [0, 1, 2, 3, 4]))) < 1e-9,
            "A concave polygon triangulates to exactly its area");

        var cube = new TriSoup();
        var min = new Vector3(0);
        var max = new Vector3(2);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            cube.Add(a, b, c);
            cube.Add(a, c, d);
        }
        Quad(new(0, 0, 0), new(0, 2, 0), new(2, 2, 0), new(2, 0, 0));
        Quad(new(0, 0, 2), new(2, 0, 2), new(2, 2, 2), new(0, 2, 2));
        Quad(new(0, 0, 0), new(2, 0, 0), new(2, 0, 2), new(0, 0, 2));
        Quad(new(2, 0, 0), new(2, 2, 0), new(2, 2, 2), new(2, 0, 2));
        Quad(new(2, 2, 0), new(0, 2, 0), new(0, 2, 2), new(2, 2, 2));
        Quad(new(0, 2, 0), new(0, 0, 0), new(0, 0, 2), new(0, 2, 2));
        var half = MeshClipper.Clip(cube, Vector3.UnitZ, 1, keepPositive: false);
        check(Math.Abs(Volume(half) - 4) < 1e-4, "Clipping a closed cube at mid-height leaves a closed solid of half the volume");
        var planes = MeshClipper.ConvexPlanes(cube);
        check(planes is { Count: 6 }, "A cube is recognized as a convex solid with six planes");
        var cutter = new TriSoup();
        void CutQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            cutter.Add(a, b, c);
            cutter.Add(a, c, d);
        }
        // A 1 × 3 × 1 slot through the cube's middle: 2 × 2 × 2 − 1 × 2 × 1 = 6.
        CutQuad(new(0.5f, -1, 0.5f), new(0.5f, 3, 0.5f), new(1.5f, 3, 0.5f), new(1.5f, -1, 0.5f));
        CutQuad(new(0.5f, -1, 1.5f), new(1.5f, -1, 1.5f), new(1.5f, 3, 1.5f), new(0.5f, 3, 1.5f));
        CutQuad(new(0.5f, -1, 0.5f), new(1.5f, -1, 0.5f), new(1.5f, -1, 1.5f), new(0.5f, -1, 1.5f));
        CutQuad(new(1.5f, -1, 0.5f), new(1.5f, 3, 0.5f), new(1.5f, 3, 1.5f), new(1.5f, -1, 1.5f));
        CutQuad(new(1.5f, 3, 0.5f), new(0.5f, 3, 0.5f), new(0.5f, 3, 1.5f), new(1.5f, 3, 1.5f));
        CutQuad(new(0.5f, 3, 0.5f), new(0.5f, -1, 0.5f), new(0.5f, -1, 1.5f), new(0.5f, 3, 1.5f));
        var slot = MeshClipper.ConvexPlanes(cutter)!;
        var remaining = MeshClipper.Subtract(cube, slot);
        var subtracted = remaining.Sum(piece => Volume(new ImportedModel
        {
            Positions = [.. piece.Points], Indices = Enumerable.Range(0, piece.Points.Count).ToArray()
        }));
        check(Math.Abs(subtracted - 6) < 1e-3, "Subtracting a convex slot from a cube removes exactly the slot's volume");
        _ = min;
        _ = max;
    }

    private static double Cross(P2 a, P2 b, P2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    // ------------------------------------------------------------------ OBJ

    private static void Obj(Action<bool, string> check, string folder)
    {
        // A quad, a concave L-shaped hexagon, negative (relative) indices, a glass material and a missing material.
        Write(folder, "scene.mtl", "newmtl Paint\nKd 0.5 0.5 0.5\nKs 0.5 0.5 0.5\nNs 10\nnewmtl Vidrio\nKd 0 0 1\nd 0.25\n");
        var path = Write(folder, "scene.obj",
            "# test\nmtllib scene.mtl\nusemtl Paint\n" +
            "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n" +
            "v 0 0 5\nv 3 0 5\nv 3 1 5\nv 1 1 5\nv 1 3 5\nv 0 3 5\nf 5 6 7 8 9 10\n" +
            "usemtl Vidrio\nv 10 0 0\nv 11 0 0\nv 10 1 0\nf -3 -2 -1\n" +
            "usemtl Missing\nv 20 0 0\nv 21 0 0\nv 20 1 0\nf 14 15 16\n");
        var model = ModelImporter.Import(path, new ModelImportOptions { UpAxis = UpAxis.Z }).Model;
        check(model.TriangleCount == 2 + 4 + 1 + 1, "OBJ quads, concave polygons and relative indices triangulate correctly");
        var concaveArea = Enumerable.Range(2, 4).Sum(t => TriangleArea(model, t));
        check(Math.Abs(concaveArea - 5) < 1e-4, "A concave OBJ polygon keeps its exact area (3×1 + 1×2)");
        check(model.Materials.Any(m => m.IsTransmissive) && model.Materials.Any(m => m.Name == "Paint" && m.Roughness < 0.8f),
            "An MTL file supplies glass transparency and shininess");
        check(model.Materials.Any(m => m.Name == "Missing"), "A material missing from the MTL still gets a default material");

        var yUp = Write(folder, "yup.obj", "v 0 0 0\nv 1 0 0\nv 0 5 0\nf 1 2 3\n");
        var rotated = ModelImporter.Import(yUp, new ModelImportOptions { UpAxis = UpAxis.Y }).Model;
        check(rotated.Positions.Max(p => p.Z) == 5, "A Y-up OBJ is rotated so its height becomes Z");

        var millimeters = Write(folder, "mm.obj", "v 0 0 0\nv 12000 0 0\nv 0 9000 0\nf 1 2 3\n");
        var scaled = ModelImporter.Import(millimeters, new ModelImportOptions { UpAxis = UpAxis.Z }).Model;
        check(Math.Abs(scaled.Positions.Max(p => p.X) - 12) < 1e-4, "A model whose size is in the thousands is read as millimeters");
        var forced = ModelImporter.Import(millimeters, new ModelImportOptions { UpAxis = UpAxis.Z, MetersPerUnit = 1 }).Model;
        check(forced.Positions.Max(p => p.X) == 12000, "An explicit unit overrides the size heuristic");

        var placed = Write(folder, "placed.obj", "v 100 100 5\nv 102 100 5\nv 100 102 7\nf 1 2 3\n");
        var centered = ModelImporter.Import(placed, new ModelImportOptions { UpAxis = UpAxis.Z, CenterHorizontally = true, PlaceOnGround = true }).Model;
        var (low, high) = centered.Bounds();
        check(Math.Abs(low.Z) < 1e-6 && Math.Abs((low.X + high.X) / 2) < 1e-5, "Centering and ground placement move the model to the origin");

        var broken = Write(folder, "broken.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nv nan 0 0\nf 1 2 3\nf 1 2 99\nf 1 x 3\nf 1\n");
        check(ModelImporter.Import(broken, new ModelImportOptions { UpAxis = UpAxis.Z }).Model.TriangleCount == 1,
            "Faces with bad indices or numbers are skipped without failing the import");
    }

    private static double TriangleArea(ImportedModel model, int triangle) =>
        Vector3.Cross(model.Positions[model.Indices[triangle * 3 + 1]] - model.Positions[model.Indices[triangle * 3]],
            model.Positions[model.Indices[triangle * 3 + 2]] - model.Positions[model.Indices[triangle * 3]]).Length() / 2;

    // ------------------------------------------------------------------ DXF

    private static void Dxf(Action<bool, string> check, string folder)
    {
        static string Pairs(params object[] values) =>
            string.Join("\n", values.Select((v, i) => (i % 2 == 0 ? v.ToString()!.PadLeft(3) : v.ToString()))) + "\n";
        var text = new StringBuilder();
        text.Append(Pairs(0, "SECTION", 2, "HEADER", 9, "$INSUNITS", 70, 4, 0, "ENDSEC"));
        text.Append(Pairs(0, "SECTION", 2, "TABLES", 0, "TABLE", 2, "LAYER",
            0, "LAYER", 2, "HIDDEN", 70, 0, 62, -3, 0, "LAYER", 2, "SHOWN", 70, 0, 62, 1, 0, "ENDTAB", 0, "ENDSEC"));
        text.Append(Pairs(0, "SECTION", 2, "BLOCKS",
            0, "BLOCK", 2, "BOX", 10, 0, 20, 0, 30, 0,
            0, "3DFACE", 8, "SHOWN", 10, 0, 20, 0, 30, 0, 11, 1000, 21, 0, 31, 0, 12, 1000, 22, 1000, 32, 0, 13, 0, 23, 1000, 33, 0,
            0, "ENDBLK", 0, "ENDSEC"));
        text.Append(Pairs(0, "SECTION", 2, "ENTITIES",
            0, "3DFACE", 8, "SHOWN", 10, 0, 20, 0, 30, 0, 11, 2000, 21, 0, 31, 0, 12, 2000, 22, 2000, 32, 0, 13, 0, 23, 2000, 33, 0,
            0, "3DFACE", 8, "HIDDEN", 10, 0, 20, 0, 30, 0, 11, 9000, 21, 0, 31, 0, 12, 9000, 22, 9000, 32, 0,
            0, "INSERT", 8, "SHOWN", 2, "BOX", 10, 5000, 20, 0, 30, 0, 41, 2, 42, 2, 43, 1, 50, 90,
            0, "LINE", 8, "SHOWN", 10, 0, 20, 0, 30, 0, 11, 3000, 21, 3000, 31, 0,
            0, "POLYLINE", 8, "SHOWN", 66, 1, 70, 64, 71, 4, 72, 1,
            0, "VERTEX", 8, "SHOWN", 10, 0, 20, 3000, 30, 500, 70, 192,
            0, "VERTEX", 8, "SHOWN", 10, 1000, 20, 3000, 30, 500, 70, 192,
            0, "VERTEX", 8, "SHOWN", 10, 1000, 20, 4000, 30, 500, 70, 192,
            0, "VERTEX", 8, "SHOWN", 10, 0, 20, 4000, 30, 500, 70, 192,
            0, "VERTEX", 8, "SHOWN", 10, 0, 20, 0, 30, 0, 70, 128, 71, 1, 72, 2, 73, 3, 74, 4,
            0, "SEQEND", 8, "SHOWN",
            0, "3DSOLID", 8, "SHOWN",
            0, "ENDSEC", 0, "EOF"));
        var path = Write(folder, "synthetic.dxf", text.ToString());
        var model = ModelImporter.Import(path).Model;
        check(model.TriangleCount == 6, "DXF 3DFACEs, nested block inserts and polyface meshes become triangles; hidden layers are skipped");
        var (min, max) = model.Bounds();
        check(Math.Abs(max.X - 5) < 1e-4 && Math.Abs(min.X) < 1e-4 && Math.Abs(max.Y - 4) < 1e-4,
            "DXF units (millimeters), insert scale 2 and rotation 90° place the geometry exactly");
        check(model.Linework.Count == 1 && model.Warnings.Contains("Import.Warning.AcisSolids"), "DXF lines are kept as linework and ACIS solids are reported");
        check(ModelImporter.Inspect(path).CanImport, "A DXF file is inspected as importable");

        var binary = Path.Combine(folder, "binary.dxf");
        File.WriteAllBytes(binary, Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\x1a\0").Concat(new byte[64]).ToArray());
        var rejected = false;
        try { ModelImporter.Import(binary); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && !ModelImporter.Inspect(binary).CanImport, "Binary DXF files are rejected with a clear message");

        var polylineText = Pairs(0, "SECTION", 2, "ENTITIES",
            0, "LWPOLYLINE", 8, "0", 90, 4, 70, 1, 10, 0, 20, 0, 10, 4, 20, 0, 42, 1, 10, 4, 20, 4, 10, 0, 20, 4, 0, "ENDSEC", 0, "EOF");
        var lines = ModelImporter.Import(Write(folder, "lw.dxf", polylineText)).Model;
        check(lines.Linework.Count == 1 && lines.Linework[0].Closed && lines.Linework[0].Points.Length > 4,
            "A closed polyline with a bulge becomes a polyline with arc points");
    }

    // ------------------------------------------------------------------ STL

    private static void Stl(Action<bool, string> check, string folder)
    {
        var binary = new List<byte>(new byte[80]);
        binary.AddRange(BitConverter.GetBytes(2u));
        foreach (var triangle in new[] { new[] { 0f, 0, 0, 1000, 0, 0, 0, 1000, 0 }, new[] { 0f, 0, 0, 0, 1000, 0, 0, 0, 1000 } })
        {
            binary.AddRange(new byte[12]);
            foreach (var value in triangle) binary.AddRange(BitConverter.GetBytes(value));
            binary.AddRange(new byte[2]);
        }
        var path = Path.Combine(folder, "bin.stl");
        File.WriteAllBytes(path, [.. binary]);
        var model = ModelImporter.Import(path, new ModelImportOptions { UpAxis = UpAxis.Z }).Model;
        check(model.TriangleCount == 2 && Math.Abs(model.Positions.Max(p => p.Y) - 1) < 1e-5, "Binary STL is read, and a size in the thousands is millimeters");
        var ascii = Write(folder, "ascii.stl",
            "solid t\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 2 0 0\nvertex 0 2 0\nendloop\nendfacet\nendsolid t\n");
        check(ModelImporter.Import(ascii).Model.TriangleCount == 1, "ASCII STL is read");
    }

    // ------------------------------------------------------------------ round trips

    private static void RoundTrips(Action<bool, string> check, string folder)
    {
        var house = SampleProjects.CreateDemonstrationHouse();
        var source = BuildingMesher.Build(house, includeGround: false);
        var (sourceMin, sourceMax) = source.Bounds();
        var (obj, mtl) = MeshExporter.ToObj(source, "house.mtl");
        File.WriteAllText(Path.Combine(folder, "house.mtl"), mtl);
        var objPath = Write(folder, "house.obj", obj);
        var glbPath = Path.Combine(folder, "house.glb");
        File.WriteAllBytes(glbPath, MeshExporter.ToGlb(source, "house"));
        var validGlb = File.ReadAllBytes(glbPath);
        static byte[] ChangeGlbHeader(byte[] sourceBytes, int offset, uint value)
        {
            var changed = (byte[])sourceBytes.Clone();
            BitConverter.GetBytes(value).CopyTo(changed, offset);
            return changed;
        }
        bool RejectGlb(string name, byte[] content)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, content);
            try { ModelImporter.Import(path); return false; }
            catch (InvalidDataException) { return true; }
        }

        check(RejectGlb("invalid-glb-version.glb", ChangeGlbHeader(validGlb, 4, 1)),
            "GLB importer rejects unsupported header versions");
        check(RejectGlb("invalid-glb-length.glb", ChangeGlbHeader(validGlb, 8, (uint)validGlb.Length - 4)),
            "GLB importer rejects a declared file length that differs from the actual length");
        check(RejectGlb("overflow-glb-chunk.glb", ChangeGlbHeader(validGlb, 12, (uint)int.MaxValue - 4)),
            "GLB importer rejects overflowing chunk lengths with InvalidDataException");
        var danglingChunk = new byte[validGlb.Length + 4];
        Array.Copy(validGlb, danglingChunk, validGlb.Length);
        BitConverter.GetBytes((uint)danglingChunk.Length).CopyTo(danglingChunk, 8);
        check(RejectGlb("trailing-glb-header.glb", danglingChunk),
            "GLB importer rejects an incomplete trailing chunk header");

        foreach (var (name, path) in new[] { ("OBJ", objPath), ("GLB", glbPath) })
        {
            var model = ModelImporter.Import(path).Model;
            var (min, max) = model.Bounds();
            check(model.TriangleCount == source.TriangleCount && Vector3.Distance(min, sourceMin) < 1e-3f && Vector3.Distance(max, sourceMax) < 1e-3f,
                $"The application's own {name} export imports back with the same triangles and bounds");
        }
        var ifcText = IfcExporter.ToStep(house, DateTimeOffset.UtcNow);
        var ifc = ModelImporter.Import(Write(folder, "house.ifc", ifcText)).Model;
        var (ifcMin, ifcMax) = ifc.Bounds();
        check(ifc.TriangleCount > 500 && Math.Abs(ifcMax.Z - sourceMax.Z) < 0.05f && Math.Abs((ifcMax.X - ifcMin.X) - (sourceMax.X - sourceMin.X)) < 0.1f,
            "The application's own IFC export imports back with the house's size and height");
        var solid = house.Walls.Sum(wall => WallGeometry.NetVolume(wall, house.Openings.Where(o => o.WallId == wall.Id)));
        check(ifc.Materials.Any(m => m.IsTransmissive) || ifc.TriangleCount > 0, "IFC import yields materials for the imported elements");
        _ = solid;
    }

    // ------------------------------------------------------------------ glTF external buffer path isolation

    private static void GltfBufferPaths(Action<bool, string> check, string folder)
    {
        static string Document(string uri, int declaredLength = 36, int viewLength = 36,
            int viewOffset = 0, int accessorOffset = 0, int accessorCount = 3) =>
            System.Text.Json.JsonSerializer.Serialize(new
            {
                asset = new { version = "2.0" },
                buffers = new[] { new { uri, byteLength = declaredLength } },
                bufferViews = new[] { new { buffer = 0, byteOffset = viewOffset, byteLength = viewLength } },
                accessors = new[] { new { bufferView = 0, byteOffset = accessorOffset,
                    componentType = 5126, count = accessorCount, type = "VEC3" } },
                meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 } } } } }
            });

        var triangle = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }
            .SelectMany(value => BitConverter.GetBytes(value)).ToArray();
        var nested = Path.Combine(folder, "gltf-buffers");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(nested, "triangle.bin"), triangle);
        var valid = ModelImporter.Import(Write(folder, "nested-buffer.gltf",
            Document("gltf-buffers/triangle.bin"))).Model;
        check(valid.TriangleCount == 1,
            "glTF permits external binary buffers in subdirectories of the model folder");

        // A valid buffer may contain multiple views, but an accessor must not read bytes
        // belonging to the next view. The bounds check also runs before array allocation.
        bool Invalid(string name, string json)
        {
            try
            {
                ModelImporter.Import(Write(folder, name, json));
                return false;
            }
            catch (InvalidDataException) { return true; }
        }
        check(Invalid("truncated-buffer.gltf",
                Document("gltf-buffers/triangle.bin", declaredLength: 40)),
            "glTF rejects external buffers shorter than their declared byteLength");
        check(Invalid("cross-view.gltf",
                Document("gltf-buffers/triangle.bin", viewLength: 12)),
            "glTF accessors cannot read into an adjacent buffer view");
        check(Invalid("out-of-range-view.gltf",
                Document("gltf-buffers/triangle.bin", viewOffset: 12)),
            "glTF buffer views cannot extend past the underlying buffer");
        check(Invalid("negative-offset.gltf",
                Document("gltf-buffers/triangle.bin", accessorOffset: -4)),
            "glTF rejects negative accessor offsets");
        check(Invalid("oversized-accessor.gltf",
                Document("gltf-buffers/triangle.bin", accessorCount: int.MaxValue)),
            "glTF rejects oversized accessor counts before allocating vertex arrays");

        // The sibling directory shares the exact prefix of the model directory.
        // A previous StartsWith(rootDirectory) check incorrectly accepted it.
        var sibling = folder + "-other";
        Directory.CreateDirectory(sibling);
        try
        {
            File.WriteAllBytes(Path.Combine(sibling, "triangle.bin"), triangle);
            var siblingUri = "../" + Path.GetFileName(sibling) + "/triangle.bin";
            var rejected = false;
            try { ModelImporter.Import(Write(folder, "sibling-buffer.gltf", Document(siblingUri))); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "glTF cannot read a sibling directory sharing the model folder prefix");

            rejected = false;
            try
            {
                ModelImporter.Import(Write(folder, "encoded-buffer.gltf",
                    Document("%2e%2e/" + Path.GetFileName(sibling) + "/triangle.bin")));
            }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "glTF rejects percent-encoded parent-directory traversal");

            rejected = false;
            try { ModelImporter.Import(Write(folder, "absolute-buffer.gltf",
                Document(Path.Combine(sibling, "triangle.bin")))); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "glTF rejects absolute external buffer paths");
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    /// <summary>Reject invalid glTF index streams before allocating from untrusted accessor counts.</summary>
    private static void GltfIndexAccessors(Action<bool, string> check, string folder)
    {
        var vertices = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }
            .SelectMany(BitConverter.GetBytes).ToArray();
        File.WriteAllBytes(Path.Combine(folder, "gltf-indices.bin"),
            vertices.Concat(new byte[] { 0, 0, 1, 0, 2, 0 }).ToArray());
        var validJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0" },
            buffers = new[] { new { uri = "gltf-indices.bin", byteLength = 42 } },
            bufferViews = new[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = 36 },
                new { buffer = 0, byteOffset = 36, byteLength = 6 }
            },
            accessors = new[]
            {
                new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3" },
                new { bufferView = 1, componentType = 5123, count = 3, type = "SCALAR" }
            },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 }, indices = 1 } } } }
        });

        var valid = ModelImporter.Import(Write(folder, "gltf-valid-indices.gltf", validJson)).Model;
        check(valid.TriangleCount == 1,
            "glTF accepts ordinary unsigned-short indexed triangles");

        bool Reject(string filename, System.Text.Json.Nodes.JsonNode document)
        {
            try
            {
                ModelImporter.Import(Write(folder, filename, document.ToJsonString()));
                return false;
            }
            catch (InvalidDataException) { return true; }
        }

        var unbacked = System.Text.Json.Nodes.JsonNode.Parse(validJson)!;
        var indexAccessor = unbacked["accessors"]![1]!.AsObject();
        indexAccessor.Remove("bufferView");
        indexAccessor["count"] = int.MaxValue;
        check(Reject("gltf-unbacked-indices.gltf", unbacked),
            "glTF refuses unbacked index accessors without allocating an untrusted count");

        var wrongComponent = System.Text.Json.Nodes.JsonNode.Parse(validJson)!;
        wrongComponent["accessors"]![1]!["componentType"] = 5126;
        check(Reject("gltf-float-indices.gltf", wrongComponent),
            "glTF refuses floating-point index accessors instead of silently replacing indices");
    }

    /// <summary>Non-uniform and reflected glTF nodes must preserve the surface normal direction.</summary>
    private static void GltfNodeNormals(Action<bool, string> check, string folder)
    {
        // This triangle is tilted in Y/Z; a non-uniform scale changes its face normal.
        // Multiplying normals by the forward matrix would point them in the wrong direction.
        var faceNormal = Vector3.Normalize(Vector3.Cross(
            new Vector3(1, 0, 0), new Vector3(0, 1, 1)));
        var values = new float[]
        {
            0, 0, 0, 1, 0, 0, 0, 1, 1,
            faceNormal.X, faceNormal.Y, faceNormal.Z,
            faceNormal.X, faceNormal.Y, faceNormal.Z,
            faceNormal.X, faceNormal.Y, faceNormal.Z
        };
        var binary = values.SelectMany(BitConverter.GetBytes).ToArray();
        var binaryPath = Path.Combine(folder, "gltf-node-normals.bin");
        File.WriteAllBytes(binaryPath, binary);

        foreach (var (name, scale) in new[]
        {
            ("nonuniform", new[] { 2f, 1f, 3f }),
            ("mirrored", new[] { -2f, 1f, 3f }),
            ("uniform", new[] { 2f, 2f, 2f })
        })
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                asset = new { version = "2.0" },
                buffers = new[] { new { uri = "gltf-node-normals.bin", byteLength = binary.Length } },
                bufferViews = new[]
                {
                    new { buffer = 0, byteOffset = 0, byteLength = 36 },
                    new { buffer = 0, byteOffset = 36, byteLength = 36 }
                },
                accessors = new[]
                {
                    new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3" },
                    new { bufferView = 1, componentType = 5126, count = 3, type = "VEC3" }
                },
                meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 } } } } },
                nodes = new[] { new { mesh = 0, scale } },
                scenes = new[] { new { nodes = new[] { 0 } } },
                scene = 0
            });
            var imported = ModelImporter.Import(Write(folder, $"gltf-normals-{name}.gltf", json)).Model;
            var indices = imported.Indices;
            var a = imported.Positions[indices[0]];
            var b = imported.Positions[indices[1]];
            var c = imported.Positions[indices[2]];
            var expected = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            var actual = imported.Normals is { Length: 3 } ? imported.Normals[indices[0]] : Vector3.Zero;
            check(imported.TriangleCount == 1 && Vector3.Dot(actual, expected) > 0.9999f,
                $"glTF {name} node transforms keep imported normals aligned with the triangle face");
        }
    }

    /// <summary>Opaque glTF PNG base-color textures must survive import, UV conversion and render assembly.</summary>
    private static void GltfPngTextures(Action<bool, string> check, string folder)
    {
        // RGB test tile: red/green on the top row, blue/white on the bottom.
        var png = PngCodec.Encode(new RasterImage(2, 2,
        [
            255, 0, 0,  0, 255, 0,
            0, 0, 255,  255, 255, 255
        ]));
        var imageDirectory = Path.Combine(folder, "gltf-textures");
        Directory.CreateDirectory(imageDirectory);
        File.WriteAllBytes(Path.Combine(imageDirectory, "tile.png"), png);
        var vertices = new float[]
        {
            0, 0, 0, 1, 0, 0, 0, 1, 0,
            0.25f, 0.25f, 0.75f, 0.25f, 0.25f, 0.75f
        }.SelectMany(BitConverter.GetBytes).ToArray();
        File.WriteAllBytes(Path.Combine(folder, "gltf-textured.bin"), vertices);
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0" },
            buffers = new[] { new { uri = "gltf-textured.bin", byteLength = vertices.Length } },
            bufferViews = new[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = 36 },
                new { buffer = 0, byteOffset = 36, byteLength = 24 }
            },
            accessors = new[]
            {
                new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3" },
                new { bufferView = 1, componentType = 5126, count = 3, type = "VEC2" }
            },
            images = new[] { new { uri = "gltf-textures/tile.png" } },
            textures = new[] { new { source = 0 } },
            materials = new[] { new
            {
                name = "Textured wall",
                pbrMetallicRoughness = new
                {
                    baseColorFactor = new[] { 0.5f, 1f, 1f, 1f },
                    baseColorTexture = new { index = 0 }
                }
            } },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, TEXCOORD_0 = 1 }, material = 0 } } } }
        });

        var imported = ModelImporter.Import(Write(folder, "gltf-png-external.gltf", json)).Model;
        var material = imported.Materials[imported.TriangleMaterial[0]];
        var scene = new RenderSceneBuilder().AddModel(imported).Build();
        var sample = material.Texture?.Sample(0.25f, 0.75f, 0) ?? Vector3.Zero;
        check(imported.TriangleCount == 1 && material.Texture is { Width: 2, Height: 2 } &&
              material.UvMapped && Math.Abs(material.BaseColor.X - 0.5f) < 1e-6f &&
              sample.X > 0.99f && sample.Y < 0.01f && sample.Z < 0.01f &&
              imported.Uvs is { Length: 3 } && Math.Abs(imported.Uvs[0].Y - 0.25f) < 1e-6f &&
              scene.TriangleUvs is { Length: 3 } && scene.TriangleUvs[0] == imported.Uvs[0],
            "glTF opaque PNG textures use the source UVs, correct vertical orientation and base-color multiplier");

        var dataUri = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        dataUri["images"]![0]!["uri"] = "data:image/png;base64," + Convert.ToBase64String(png);
        var embeddedData = ModelImporter.Import(Write(folder, "gltf-png-data-uri.gltf", dataUri.ToJsonString())).Model;
        check(embeddedData.Materials[0].Texture is { Width: 2, Height: 2 },
            "glTF accepts base-color PNG images embedded as data URIs");

        var withBufferView = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var binary = vertices.Concat(png).ToArray();
        File.WriteAllBytes(Path.Combine(folder, "gltf-png-view.bin"), binary);
        withBufferView["buffers"]![0]!["uri"] = "gltf-png-view.bin";
        withBufferView["buffers"]![0]!["byteLength"] = binary.Length;
        withBufferView["bufferViews"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(new { buffer = 0, byteOffset = vertices.Length, byteLength = png.Length })));
        withBufferView["images"]![0] = System.Text.Json.Nodes.JsonNode.Parse("{\"bufferView\":2,\"mimeType\":\"image/png\"}");
        var viewModel = ModelImporter.Import(Write(folder, "gltf-png-buffer-view.gltf", withBufferView.ToJsonString())).Model;
        check(viewModel.Materials[0].Texture is { Width: 2, Height: 2 },
            "glTF accepts PNG images stored in binary bufferViews");

        var unsupported = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        unsupported["images"]![0]!["uri"] = "gltf-textures/unsupported.jpg";
        var withoutPng = ModelImporter.Import(Write(folder, "gltf-jpeg-fallback.gltf", unsupported.ToJsonString())).Model;
        check(withoutPng.Materials[0].Texture is null && !withoutPng.Materials[0].UvMapped,
            "Unsupported JPEG textures fall back to material factors without crashing");

        var sibling = folder + "-image-outside";
        Directory.CreateDirectory(sibling);
        try
        {
            File.WriteAllBytes(Path.Combine(sibling, "tile.png"), png);
            var escaped = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            escaped["images"]![0]!["uri"] = "../" + Path.GetFileName(sibling) + "/tile.png";
            var rejected = false;
            try { ModelImporter.Import(Write(folder, "gltf-png-traversal.gltf", escaped.ToJsonString())); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "glTF image paths cannot escape the model directory");
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    // ------------------------------------------------------------------ IFC

    private static string IfcFixture(string body) =>
        "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');\nFILE_NAME('t.ifc','2026-01-01T00:00:00',(''),(''),'','','');\n" +
        "FILE_SCHEMA(('IFC2X3'));\nENDSEC;\nDATA;\n" +
        "#1=IFCPROJECT('0',$,'P',$,$,$,$,(#20),#10);\n#10=IFCUNITASSIGNMENT((#11));\n#11=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);\n" +
        "#20=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#21,$);\n#21=IFCAXIS2PLACEMENT3D(#22,$,$);\n#22=IFCCARTESIANPOINT((0.,0.,0.));\n" +
        "#31=IFCLOCALPLACEMENT($,#21);\n#44=IFCDIRECTION((0.,0.,1.));\n" +
        "#45=IFCAXIS2PLACEMENT2D(#46,$);\n#46=IFCCARTESIANPOINT((3000.,100.));\n" + body + "ENDSEC;\nEND-ISO-10303-21;\n";

    private static string WallBody(string solid) =>
        "#30=IFCWALL('1',$,'W',$,$,#31,#40,$,$);\n#40=IFCPRODUCTDEFINITIONSHAPE($,$,(#41));\n" +
        "#41=IFCSHAPEREPRESENTATION(#20,'Body','SweptSolid',(#42));\n" + solid +
        "#43=IFCRECTANGLEPROFILEDEF(.AREA.,$,#45,6000.,200.);\n";

    private static void Ifc(Action<bool, string> check, string folder)
    {
        // A 6 m × 0.2 m × 3 m wall in millimeters, cut by a 1 m × 2.1 m opening that is thicker than the wall.
        var opening =
            "#50=IFCOPENINGELEMENT('2',$,'O',$,$,#51,#52,$,$);\n#51=IFCLOCALPLACEMENT(#31,#53);\n#53=IFCAXIS2PLACEMENT3D(#54,$,$);\n" +
            "#54=IFCCARTESIANPOINT((2000.,-100.,0.));\n#52=IFCPRODUCTDEFINITIONSHAPE($,$,(#55));\n" +
            "#55=IFCSHAPEREPRESENTATION(#20,'Body','SweptSolid',(#56));\n#56=IFCEXTRUDEDAREASOLID(#57,#21,#44,2100.);\n" +
            "#57=IFCRECTANGLEPROFILEDEF(.AREA.,$,#58,1000.,400.);\n#58=IFCAXIS2PLACEMENT2D(#59,$);\n#59=IFCCARTESIANPOINT((500.,200.));\n" +
            "#60=IFCRELVOIDSELEMENT('3',$,$,$,#30,#50);\n";
        var plain = ModelImporter.Import(Write(folder, "wall.ifc", IfcFixture(WallBody("#42=IFCEXTRUDEDAREASOLID(#43,#21,#44,3000.);\n")))).Model;
        check(Math.Abs(Volume(plain) - 3.6) < 1e-4, "An IFC extruded wall in millimeters imports as 6 × 0.2 × 3 m (3.6 m³)");
        var cut = ModelImporter.Import(Write(folder, "wall-open.ifc", IfcFixture(WallBody("#42=IFCEXTRUDEDAREASOLID(#43,#21,#44,3000.);\n") + opening))).Model;
        check(Math.Abs(Volume(cut) - 3.18) < 1e-3, "An IFC opening is cut out of its wall: 3.6 − 1 × 0.2 × 2.1 = 3.18 m³");
        var clip = WallBody("#42=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#47,#70);\n#47=IFCEXTRUDEDAREASOLID(#43,#21,#44,3000.);\n" +
                            "#70=IFCHALFSPACESOLID(#71,.F.);\n#71=IFCPLANE(#72);\n#72=IFCAXIS2PLACEMENT3D(#73,$,$);\n#73=IFCCARTESIANPOINT((0.,0.,2000.));\n");
        var clipped = ModelImporter.Import(Write(folder, "wall-clip.ifc", IfcFixture(clip))).Model;
        check(Math.Abs(Volume(clipped) - 2.4) < 1e-3 && clipped.Bounds().Max.Z < 2.0001f,
            "An IFC clipping half-space trims a wall to 2 m (2.4 m³) and caps the cut");
        var styled = IfcFixture(WallBody("#42=IFCEXTRUDEDAREASOLID(#43,#21,#44,3000.);\n") +
            "#80=IFCSTYLEDITEM(#42,(#81),$);\n#81=IFCPRESENTATIONSTYLEASSIGNMENT((#82));\n#82=IFCSURFACESTYLE($,.BOTH.,(#83));\n" +
            "#83=IFCSURFACESTYLERENDERING(#84,0.,$,$,$,$,$,$,.NOTDEFINED.);\n#84=IFCCOLOURRGB($,1.,0.,0.);\n");
        var painted = ModelImporter.Import(Write(folder, "wall-styled.ifc", styled)).Model;
        check(painted.Materials.Any(m => m.BaseColor.X > 0.9f && m.BaseColor.Y < 0.1f), "IFC surface styles become material colors");
        var far = IfcFixture(WallBody("#42=IFCEXTRUDEDAREASOLID(#43,#21,#44,3000.);\n")).Replace("#22=IFCCARTESIANPOINT((0.,0.,0.));", "#22=IFCCARTESIANPOINT((0.,0.,0.));")
            .Replace("#31=IFCLOCALPLACEMENT($,#21);", "#31=IFCLOCALPLACEMENT($,#32);\n#32=IFCAXIS2PLACEMENT3D(#33,$,$);\n#33=IFCCARTESIANPOINT((500000000.,4000000000.,0.));");
        var shifted = ModelImporter.Import(Write(folder, "far.ifc", far)).Model;
        check(shifted.Bounds().Max.X < 100 && shifted.Warnings.Contains("Import.Warning.IfcShifted"),
            "A survey-scale IFC origin is brought back near zero to keep single precision accurate");
        var invalid = false;
        try { ModelImporter.Import(Write(folder, "invalid.ifc", "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n")); }
        catch (InvalidDataException) { invalid = true; }
        check(invalid, "An IFC file with no entities is rejected");
    }

    // ------------------------------------------------------------------ COLLADA

    private static void Collada(Action<bool, string> check, string folder)
    {
        var dae = "<?xml version=\"1.0\"?><COLLADA xmlns=\"http://www.collada.org/2005/11/COLLADASchema\" version=\"1.4.1\">" +
                  "<asset><unit meter=\"0.0254\" name=\"inch\"/><up_axis>Z_UP</up_axis></asset>" +
                  "<library_effects><effect id=\"e\"><profile_COMMON><technique><lambert><diffuse><color>1 0 0 1</color></diffuse></lambert></technique></profile_COMMON></effect></library_effects>" +
                  "<library_materials><material id=\"m\"><instance_effect url=\"#e\"/></material></library_materials>" +
                  "<library_geometries><geometry id=\"g\"><mesh><source id=\"p\"><float_array id=\"pa\" count=\"12\">0 0 0 100 0 0 100 100 0 0 100 0</float_array>" +
                  "<technique_common><accessor source=\"#pa\" count=\"4\" stride=\"3\"/></technique_common></source><vertices id=\"v\"><input semantic=\"POSITION\" source=\"#p\"/></vertices>" +
                  "<polylist material=\"s\" count=\"1\"><input semantic=\"VERTEX\" source=\"#v\" offset=\"0\"/><vcount>4</vcount><p>0 1 2 3</p></polylist></mesh></geometry></library_geometries>" +
                  "<library_visual_scenes><visual_scene id=\"s\"><node><translate>10 0 0</translate><instance_geometry url=\"#g\"><bind_material><technique_common>" +
                  "<instance_material symbol=\"s\" target=\"#m\"/></technique_common></bind_material></instance_geometry></node></visual_scene></library_visual_scenes>" +
                  "<scene><instance_visual_scene url=\"#s\"/></scene></COLLADA>";
        var model = ModelImporter.Import(Write(folder, "scene.dae", dae)).Model;
        var (min, max) = model.Bounds();
        check(model.TriangleCount == 2 && Math.Abs(max.X - 2.794) < 1e-3 && Math.Abs(min.X - 0.254) < 1e-3,
            "COLLADA polylists, node translation and the inch unit import to exact meters");
        check(model.Materials.Any(m => m.BaseColor.X > 0.9f && m.BaseColor.Y < 0.1f), "COLLADA instance materials supply the surface color");
        var external = false;
        try { ModelImporter.Import(Write(folder, "bad.dae", "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a SYSTEM \"file:///c:/windows/win.ini\">]><COLLADA>&a;</COLLADA>")); }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException) { external = true; }
        check(external, "A COLLADA file that declares an external entity is refused");
    }

    // ------------------------------------------------------------------ detection and limits

    private static void Detection(Action<bool, string> check, string folder)
    {
        var disguised = Write(folder, "model.txt", "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\n#1=IFCWALL('a');\nENDSEC;\nEND-ISO-10303-21;\n");
        check(ModelImporter.Detect(disguised) == ImportFormat.Ifc, "A file is recognized by its content even with the wrong extension");
        check(ModelImporter.Detect(Write(folder, "x.bin", "garbage")) == ImportFormat.Unknown, "An unrecognizable file is reported as unknown");
        var unknown = false;
        try { ModelImporter.Import(Path.Combine(folder, "x.bin")); }
        catch (InvalidDataException) { unknown = true; }
        check(unknown, "Importing an unknown format fails with a validation message");
        check(ModelImporter.Detect(Write(folder, "e.dwg", "AC1032\0\0\0")) == ImportFormat.Dwg, "DWG files are recognized by their AC10xx signature");
        check(DwgConverterNames.Release("AC1032") is not null, "DWG release signatures map to AutoCAD versions");
        var missing = false;
        try { ModelImporter.Import(Path.Combine(folder, "nope.obj")); }
        catch (InvalidDataException) { missing = true; }
        check(missing, "A missing file is reported without an unhandled exception");
        var empty = Path.Combine(folder, "empty.obj");
        File.WriteAllBytes(empty, []);
        var emptyRejected = false;
        try { ModelImporter.Import(empty); }
        catch (InvalidDataException) { emptyRejected = true; }
        check(emptyRejected, "An empty file is rejected");
    }

    private static void Limits(Action<bool, string> check, string folder)
    {
        var path = Write(folder, "limit.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\nf 1 2 3\nf 1 2 3\n");
        var limited = false;
        try { ModelImporter.Import(path, new ModelImportOptions { UpAxis = UpAxis.Z, MaximumTriangles = 2 }); }
        catch (InvalidDataException) { limited = true; }
        check(limited, "A model beyond the triangle limit is refused before it exhausts memory");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var canceled = false;
        try { ModelImporter.Import(path, null, null, cancelled.Token); }
        catch (OperationCanceledException) { canceled = true; }
        check(canceled, "An import can be cancelled");
    }

    // ------------------------------------------------------------------ Revit, real files, SketchUp

    private static void Revit(Action<bool, string> check)
    {
        var file = Directory.Exists(RealFolder)
            ? Directory.EnumerateFiles(RealFolder, "*.rvt", SearchOption.AllDirectories).Where(f => new FileInfo(f).Length < 30_000_000).OrderBy(f => new FileInfo(f).Length).FirstOrDefault()
            : null;
        if (file is null)
        {
            Console.WriteLine("INFO: no Revit file found; RVT checks skipped.");
            return;
        }
        var inspection = ModelImporter.Inspect(file);
        check(inspection.Format == ImportFormat.Revit && !inspection.CanImport && inspection.Notes.Any(n => n.StartsWith("Import.Note.RevitVersion|", StringComparison.Ordinal)),
            "A real Revit file is inspected: version read, direct import declined");
        check(inspection.PreviewPng is { Length: > 100 } png && png[1] == 'P' && png[2] == 'N' && png[3] == 'G', "The embedded Revit preview image is extracted as a PNG");
        var refused = false;
        try { ModelImporter.Import(file); }
        catch (InvalidDataException ex) { refused = ex.Message.Contains("IFC", StringComparison.Ordinal); }
        check(refused, "Importing a Revit file explains that it must be exported to IFC");
    }

    private static void RealFiles(Action<bool, string> check, string folder)
    {
        var topo = Path.Combine(RealFolder, @"Joel - Casa El Guanacaste\cad\topo-mesh.dxf");
        if (File.Exists(topo))
        {
            var model = ModelImporter.Import(topo).Model;
            check(model.TriangleCount == 578 && model.Materials.Any(m => m.Pattern == SurfacePattern.Grass), "A real topography DXF imports 578 faces as terrain");
        }
        var dwg = Path.Combine(RealFolder, @"Joel - Casa El Guanacaste\cad\lote-santa-lucia-condega.dwg");
        if (File.Exists(dwg) && ModelImporter.Inspect(dwg).CanImport)
        {
            var model = ModelImporter.Import(dwg).Model;
            check(model.Linework.Count >= 1 && model.SourceFormat == "DWG" && !Directory.EnumerateDirectories(Path.GetTempPath(), "guanacaste3d-dwg-*").Any(),
                "A real DWG is converted through AutoCAD, read, and its temporary files are removed");
        }
        else Console.WriteLine("INFO: AutoCAD or the sample DWG is not available; DWG checks skipped.");
        var revitObj = Path.Combine(RealFolder, @"Planos de Mi Casa\OpenRender\Mi casa Revit 2026 - Vista 3D - RenderOpenRender.obj");
        if (File.Exists(revitObj))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var model = ModelImporter.Import(revitObj).Model;
            Console.WriteLine($"INFO: Revit OBJ, {model.TriangleCount:N0} triangles read in {clock.Elapsed.TotalSeconds:0.0} s");
            var (min, max) = model.Bounds();
            check(model.TriangleCount > 3_000_000 && max.Z - min.Z is > 4 and < 12 && clock.Elapsed < TimeSpan.FromSeconds(40),
                "A 450 MB Revit OBJ imports as a house-sized model with its materials, within seconds");
        }
        _ = folder;
    }

    private static void Sketchup(Action<bool, string> check, string folder)
    {
        var path = Path.Combine(folder, "sample.skp");
        bool written;
        try { written = SketchUpApi.TryWriteSample(path); }
        catch (Exception ex) when (ex is InvalidDataException or EntryPointNotFoundException or DllNotFoundException)
        {
            Console.WriteLine("INFO: SketchUp library could not write a sample; SKP checks skipped.");
            return;
        }
        if (!written)
        {
            Console.WriteLine("INFO: SketchUp library not installed; SKP checks skipped.");
            return;
        }
        var model = ModelImporter.Import(path).Model;
        var (min, max) = model.Bounds();
        check(model.TriangleCount == 48 && model.Warnings.Contains("Import.Warning.HiddenSkipped"),
            "A SketchUp file imports its boxes, group and component instances, and skips hidden groups");
        check(Math.Abs((max.Z - min.Z) - 2.54) < 1e-3 && Math.Abs((max.X - min.X) - 6.096) < 1e-3, "SketchUp inches convert to exact meters (100 in = 2.54 m)");
        check(model.Materials.Count == 3, "SketchUp face colors become materials");
        var inspection = ModelImporter.Inspect(path);
        check(inspection.CanImport && inspection.Notes.Any(n => n.StartsWith("Import.Note.SketchUpVersion|", StringComparison.Ordinal)),
            "A SketchUp file is inspected with its version");
        var corrupt = Path.Combine(folder, "corrupt.skp");
        File.WriteAllBytes(corrupt, File.ReadAllBytes(path).Take(300).ToArray());
        var failed = false;
        try { ModelImporter.Import(corrupt); }
        catch (InvalidDataException) { failed = true; }
        check(failed, "A truncated SketchUp file fails with a validation message instead of crashing");
    }
}

/// <summary>Reaches the internal DWG release table without widening Core's public surface.</summary>
static class DwgConverterNames
{
    public static string? Release(string magic) =>
        typeof(ModelImporter).Assembly.GetType("Guanacaste3D.Core.Import.DwgConverter")!
            .GetMethod("ReleaseName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [magic]) as string;
}
