using System.Globalization;
using System.Text;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>
/// Writes what the Rust mesher (<c>engine/crates/gr-model</c>) is verified against: sample projects saved as
/// <c>.gr</c> files, the exact mesh <see cref="BuildingMesher"/> builds from each one (<c>.refmesh</c>), and the stock
/// material of every surface kind (<c>stock-materials.tsv</c>).
///   dotnet run -c Release --project tests/GuanacasteRender.RenderBench -- --mesh-reference --out artifacts/mesh-reference
/// </summary>
static class MeshReference
{
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var projects = new (string Name, Func<BuildingDocument> Create)[]
        {
            ("demo-house", SampleProjects.CreateDemonstrationHouse),
            ("modern-house", ModernHouseShowcase.Create),
            ("showcase", ArchitecturalShowcase.Create),
            ("cobogo", () => BreezeBlockHouseShowcase.Create()),
            ("garden", Garden)
        };
        foreach (var (name, create) in projects)
        {
            var document = create();
            var path = Path.Combine(folder, name + NativeDocumentStore.RenderProjectExtension);
            NativeDocumentStore.SaveProject(path, document);
            // Mesh what a reader gets back from the file, so the reference includes the storage round trip.
            var loaded = ProjectFile.Load(path);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var mesh = BuildingMesher.Build(loaded, includeGround: true, plants: PlantDetail.Render);
            var milliseconds = clock.Elapsed.TotalMilliseconds;
            WriteMesh(Path.Combine(folder, name + ".refmesh"), mesh);
            // Plants are cached after the first build, so only the first is a fair time; it includes JIT compilation.
            Console.WriteLine($"{name,-14} {mesh.TriangleCount,9} triangles, {mesh.ElementRanges.Count,6} element ranges, first build {milliseconds:0.0} ms");
        }
        WriteMaterials(Path.Combine(folder, "stock-materials.tsv"));
    }

    /// <summary>
    /// A small plant of every growth form on an empty level: little enough geometry to keep as a test fixture, and
    /// every part of the plant generator (random sequence, trunks, branching, leaves, fronds, needles, coconuts).
    /// Identifiers are fixed, because a plant grows from its identifier.
    /// </summary>
    private static BuildingDocument Garden()
    {
        var document = BuildingDocument.Create("Jardín de referencia");
        var level = document.Levels[0].Id;
        void Plant(string id, string name, double x, double height, double spread, ElementStyle style) =>
            document.StructuralElements.Add(new StructuralElement(Guid.Parse(id), level, StructuralElementKind.Planting, name,
                new Point2(x, 1.5), new Point2(x, 1.5), spread, height, 0) { Style = style });
        Plant("0a6f3c52-7b1e-4d0a-9c35-51e7f2a4b801", "Arbusto", 0, 0.5, 0.5, ElementStyle.Shrub);
        Plant("1b7a4d63-8c2f-4e1b-ad46-62f803b5c912", "Palma", 3, 2.2, 1.6, ElementStyle.Palm);
        Plant("2c8b5e74-9d30-4f2c-be57-730914c6da23", "Conífera", 6, 1.3, 0.8, ElementStyle.Conifer);
        return document;
    }

    /// <summary>
    /// "GRMESH01", triangle count, range count; then per range a 36-character element id, first and end triangle; then
    /// nine floats per triangle (its own three corners) and one surface-kind byte per triangle. Little endian.
    /// </summary>
    private static void WriteMesh(string path, SceneMesh mesh)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII);
        writer.Write("GRMESH01"u8);
        writer.Write(mesh.TriangleCount);
        writer.Write(mesh.ElementRanges.Count);
        foreach (var range in mesh.ElementRanges)
        {
            writer.Write(Encoding.ASCII.GetBytes(range.ElementId.ToString("D")));
            writer.Write(range.First);
            writer.Write(range.End);
        }
        foreach (var index in mesh.Indices)
        {
            var position = mesh.Positions[index];
            writer.Write(position.X);
            writer.Write(position.Y);
            writer.Write(position.Z);
        }
        foreach (var kind in mesh.TriangleKinds) writer.Write((byte)kind);
    }

    private static void WriteMaterials(string path)
    {
        var text = new StringBuilder(
            "kind\tname\tbase_r\tbase_g\tbase_b\troughness\tmetallic\tior\temit_r\temit_g\temit_b\ttransmission\ttint_r\ttint_g\ttint_b\ttranslucency\talpha_cutoff\tpattern\n");
        string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        foreach (var kind in Enum.GetValues<SurfaceKind>())
        {
            var m = MaterialLibrary.ForKind(kind);
            text.Append(string.Join('\t', kind, m.Name, F(m.BaseColor.X), F(m.BaseColor.Y), F(m.BaseColor.Z), F(m.Roughness),
                F(m.Metallic), F(m.Ior), F(m.Emission.X), F(m.Emission.Y), F(m.Emission.Z), F(m.Transmission),
                F(m.TransmissionTint.X), F(m.TransmissionTint.Y), F(m.TransmissionTint.Z), F(m.Translucency),
                F(m.AlphaCutoff), (int)m.Pattern)).Append('\n');
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
}
