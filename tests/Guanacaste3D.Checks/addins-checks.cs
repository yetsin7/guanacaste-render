using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

/// <summary>Checks the family converter (models made elsewhere into .g3df families) and the model transmittal package.</summary>
static class AddInsChecks
{
    private static bool Near(double first, double second, double tolerance = 1e-3) => Math.Abs(first - second) < tolerance;

    private static bool Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return true; }
        return false;
    }

    /// <summary>A 2 × 1 × 3 box whose corner is at (10, 20, 5), with a glass top and concrete sides.</summary>
    private static string WriteBox(string folder, string name)
    {
        var path = Path.Combine(folder, name + ".obj");
        File.WriteAllText(Path.Combine(folder, name + ".mtl"), "newmtl Vidrio_azul\nKd 0.2 0.5 0.8\nnewmtl Concreto\nKd 0.6 0.6 0.6\n");
        File.WriteAllText(path, string.Join("\n",
        [
            $"mtllib {name}.mtl",
            "v 10 20 5", "v 12 20 5", "v 12 21 5", "v 10 21 5", "v 10 20 8", "v 12 20 8", "v 12 21 8", "v 10 21 8",
            "usemtl Concreto",
            "f 1 3 2", "f 1 4 3", "f 1 2 6", "f 1 6 5", "f 2 3 7", "f 2 7 6", "f 3 4 8", "f 3 8 7", "f 4 1 5", "f 4 5 8",
            "usemtl Vidrio_azul",
            "f 5 6 7", "f 5 7 8"
        ]) + "\n");
        return path;
    }

    public static void Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "g3d-addins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Converter(check, folder);
            Transmittal(check, folder);
            Library(check, folder);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void Converter(Action<bool, string> check, string folder)
    {
        check(FamilyConverter.SurfaceFor("Vidrio_azul") == SurfaceKind.Glass && FamilyConverter.SurfaceFor("Hojas verdes") == SurfaceKind.Foliage && FamilyConverter.SurfaceFor("Corteza") == SurfaceKind.Bark &&
              FamilyConverter.SurfaceFor("Acero cromado") == SurfaceKind.Metal && FamilyConverter.SurfaceFor("Concreto") == SurfaceKind.Structure && FamilyConverter.SurfaceFor(null) == SurfaceKind.Structure,
            "Materials are matched to surfaces by their names, and anything unknown is plain structure");
        check(FamilyConverter.CanConvert("a.OBJ") && FamilyConverter.CanConvert("b.glb") && !FamilyConverter.CanConvert("c.txt") && !FamilyConverter.CanConvert("d.rvt"), "Only models that can be read are offered for conversion");

        var model = WriteBox(folder, "caja");
        var target = Path.Combine(folder, "salida", "caja.g3df");
        var result = FamilyConverter.Convert(model, target, "Caja de prueba", FamilyCategory.Entourage);
        var family = NativeDocumentStore.LoadFamily(target);
        var mesh = family.Mesh!.Decode();
        var sizes = new[] { result.Width, result.Depth, result.Height }.Order().ToArray();
        check(File.Exists(target) && family.Name == "Caja de prueba" && family.Category == FamilyCategory.Entourage && result.SourceTriangles == 12 && result.Triangles == 12 &&
              Near(sizes[0], 1) && Near(sizes[1], 2) && Near(sizes[2], 3), $"A box of 1 × 2 × 3 m becomes a family of the same size: {sizes[0]:0.00} × {sizes[1]:0.00} × {sizes[2]:0.00}");
        check(Near(mesh.Min.Z, 0) && Near(mesh.Min.X + mesh.Max.X, 0) && Near(mesh.Min.Y + mesh.Max.Y, 0), "The middle of the base of the converted model is the origin of the family");
        check(mesh.Kinds.Count(kind => kind == SurfaceKind.Glass) == 2 && mesh.Kinds.Count(kind => kind == SurfaceKind.Structure) == 10, "The two glass triangles keep their surface and the ten others stay structure");

        var turned = FamilyConverter.Convert(model, Path.Combine(folder, "girada.g3df"), "Girada", FamilyCategory.Furniture, yawDegrees: 90);
        var scaled = FamilyConverter.Convert(model, Path.Combine(folder, "grande.g3df"), "Grande", FamilyCategory.Furniture, metersPerUnit: 2);
        check(Near(turned.Width, result.Depth) && Near(turned.Depth, result.Width) && Near(scaled.Height, result.Height * 2) && Near(scaled.Width, result.Width * 2),
            "Turning 90° swaps the width and the depth, and a scale of 2 doubles every size");

        var session = new DocumentSession(BuildingDocument.Create("Colocar"));
        var familyId = session.LoadComponentFamily(NativeDocumentStore.LoadFamily(target));
        var placed = session.PlaceComponentFamily(session.Document.Levels[0].Id, familyId, new Point2(1, 1));
        check(session.Document.StructuralElements.Any(item => item.Id == placed), "The converted family loads into a project and an instance of it is placed");

        // Refusals and a batch that goes on after a failure.
        check(Throws(() => FamilyConverter.Convert(Path.Combine(folder, "nada.obj"), target, "x", FamilyCategory.Entourage)) &&
              Throws(() => FamilyConverter.Convert(model, Path.Combine(folder, "x.txt"), "x", FamilyCategory.Entourage)) &&
              Throws(() => FamilyConverter.Convert(model, target, "x", FamilyCategory.TitleBlocks)) &&
              Throws(() => FamilyConverter.Convert(model, target, "x", FamilyCategory.Entourage, metersPerUnit: 0)) &&
              Throws(() => FamilyConverter.Convert(model, target, "   ", FamilyCategory.Entourage, 60000, 1, double.NaN)),
            "A missing model, a wrong extension, the title-block category, a scale of 0 and a turn that is not a number are refused");
        var empty = Path.Combine(folder, "vacio.obj");
        File.WriteAllText(empty, "# nothing here\n");
        var notes = Path.Combine(folder, "notas.txt");
        File.WriteAllText(notes, "no es un modelo");
        var otherFolder = Directory.CreateDirectory(Path.Combine(folder, "otra")).FullName;
        var duplicate = WriteBox(otherFolder, "caja");
        var batch = FamilyConverter.ConvertMany([model, empty, notes, duplicate], Path.Combine(folder, "lote"), FamilyCategory.Furniture);
        check(batch.Converted.Count == 2 && batch.Failed.Count == 2 && batch.Converted.Select(item => item.Name).Distinct().Count() == 2 && Directory.GetFiles(Path.Combine(folder, "lote"), "*.g3df").Length == 2 &&
              batch.Failed.Any(item => item.Source == empty) && batch.Failed.Any(item => item.Source == notes),
            "A batch converts what it can, gives two models of one name different files and lists the ones that failed");
        check(Throws(() => FamilyConverter.ConvertMany([], folder, FamilyCategory.Furniture)) && Throws(() => FamilyConverter.ConvertMany([model], " ", FamilyCategory.Furniture)), "A batch needs models and a folder");
    }

    private static void Transmittal(Action<bool, string> check, string folder)
    {
        var house = BreezeBlockHouseShowcase.Create();
        var plain = Path.Combine(folder, "paquete.zip");
        var report = ModelTransmittal.Create(house, plain, DateTimeOffset.Now, includeExports: false);
        check(report.Entries.Count == 1 && report.Entries[0].Role == "Project" && report.Entries[0].Path.EndsWith(".g3d", StringComparison.Ordinal) && report.Entries[0].Bytes > 1000 && File.Exists(plain), "A package without exports holds the project");
        using (var archive = ZipFile.OpenRead(plain))
        {
            var project = archive.GetEntry(report.Entries[0].Path)!;
            using var stream = project.Open();
            var recomputed = Convert.ToHexString(SHA256.HashData(stream.ReadAllBytes()));
            check(recomputed == report.Entries[0].Sha256 && archive.GetEntry(ModelTransmittal.ManifestName) is not null && archive.GetEntry(ModelTransmittal.SummaryName) is not null, "The manifest carries the true SHA-256 of the project, and the summary is in the package");
        }
        check(ModelTransmittal.Verify(plain).Count == 0, "A package that was just made verifies clean and its project opens");

        var full = Path.Combine(folder, "completo.zip");
        var withExports = ModelTransmittal.Create(house, full, DateTimeOffset.Now, includeExports: true);
        check(withExports.Entries.Count(item => item.Role == "Export") == 3 && withExports.Entries.Any(item => item.Path.EndsWith(".ifc", StringComparison.Ordinal)) &&
              withExports.Entries.Any(item => item.Path.EndsWith(".dxf", StringComparison.Ordinal)) && withExports.Entries.Any(item => item.Path.EndsWith(".pdf", StringComparison.Ordinal)) && ModelTransmittal.Verify(full).Count == 0,
            "With exports the package also holds the IFC, the DXF and the PDF");

        // Tampering shows.
        var changed = Path.Combine(folder, "cambiado.zip");
        File.Copy(full, changed);
        var ifc = withExports.Entries.First(item => item.Path.EndsWith(".ifc", StringComparison.Ordinal)).Path;
        using (var archive = ZipFile.Open(changed, ZipArchiveMode.Update))
        {
            archive.GetEntry(ifc)!.Delete();
            using var replacement = new StreamWriter(archive.CreateEntry(ifc).Open());
            replacement.Write("alterado");
        }
        check(ModelTransmittal.Verify(changed).SequenceEqual(["Changed:" + ifc]), "A file altered after packing is reported by name");
        var missing = Path.Combine(folder, "faltante.zip");
        File.Copy(full, missing);
        var dxf = withExports.Entries.First(item => item.Path.EndsWith(".dxf", StringComparison.Ordinal)).Path;
        using (var archive = ZipFile.Open(missing, ZipArchiveMode.Update)) archive.GetEntry(dxf)!.Delete();
        check(ModelTransmittal.Verify(missing).SequenceEqual(["Missing:" + dxf]), "A file that was taken out of the package is reported by name");
        var garbage = Path.Combine(folder, "basura.zip");
        File.WriteAllText(garbage, "esto no es un zip");
        var noManifest = Path.Combine(folder, "sinlista.zip");
        using (var archive = ZipFile.Open(noManifest, ZipArchiveMode.Create)) archive.CreateEntry("a.txt");
        check(ModelTransmittal.Verify(garbage).SequenceEqual(["Corrupt"]) && ModelTransmittal.Verify(noManifest).SequenceEqual(["NoManifest"]) && ModelTransmittal.Verify(Path.Combine(folder, "nunca.zip")).SequenceEqual(["NoPackage"]),
            "A file that is not a zip, a zip with no manifest and a package that is not there are each told apart");

        // Working in a team: the central model travels, and its absence is noted.
        var team = BuildingDocument.Create("Equipo");
        var central = Path.Combine(folder, "central.g3d");
        team.Collaboration = new CollaborationSettings(true, "ana", central, null);
        var without = ModelTransmittal.Create(team, Path.Combine(folder, "equipo1.zip"), DateTimeOffset.Now, false);
        ProjectFile.Save(central, BuildingDocument.Create("Equipo"));
        var with = ModelTransmittal.Create(team, Path.Combine(folder, "equipo2.zip"), DateTimeOffset.Now, false);
        check(without.Notes.Contains("CentralMissing") && without.Entries.Count == 1 && !with.Notes.Contains("CentralMissing") && with.Entries.Any(item => item.Role == "Central") && ModelTransmittal.Verify(Path.Combine(folder, "equipo2.zip")).Count == 0,
            "A shared project takes its central model along, and says so when the central model is not there");
        check(Throws(() => ModelTransmittal.Create(house, Path.Combine(folder, "no-zip.txt"), DateTimeOffset.Now, false)) && Throws(() => ModelTransmittal.Create(house, "", DateTimeOffset.Now, false)), "A package must be a .zip file");
    }

    private static void Library(Action<bool, string> check, string folder)
    {
        var library = Path.Combine(folder, "biblioteca");
        var model = WriteBox(folder, "base");
        FamilyConverter.Convert(model, Path.Combine(library, "zeta.g3df"), "Zeta", FamilyCategory.Furniture);
        FamilyConverter.Convert(model, Path.Combine(library, "muebles", "alfa.g3df"), "Alfa", FamilyCategory.Furniture);
        var house = BreezeBlockHouseShowcase.Create();
        FamilyFile.Save(Path.Combine(library, "ventana.g3df"), OpeningFamilyDocument.FromProject(house, house.OpeningTypes[0].FamilyName));
        File.WriteAllText(Path.Combine(library, "dañado.g3df"), "esto no es una familia");
        File.WriteAllText(Path.Combine(library, "notas.txt"), "no es una familia");
        var found = FamilyLibrary.Scan(library);
        check(found.Select(item => item.Name).SequenceEqual(["alfa", "ventana", "zeta"]) && found.Count(item => item.Kind == "Native") == 2 && found.Single(item => item.Name == "ventana").Kind == "Opening" && found.All(item => item.Bytes > 0),
            "The library lists the families that can be read, by name, including those in folders under it, and leaves out damaged files and other kinds");
        check(Throws(() => FamilyLibrary.Scan("")) && Throws(() => FamilyLibrary.Scan(Path.Combine(folder, "no-existe"))), "A library folder must be named and must exist");
        var published = FamilyLibrary.Publish(Path.Combine(library, "zeta.g3df"), Path.Combine(folder, "nueva"));
        var second = FamilyLibrary.Publish(Path.Combine(library, "zeta.g3df"), Path.Combine(folder, "nueva"));
        check(File.Exists(published) && Path.GetFileName(second) == "zeta 2.g3df" && FamilyLibrary.Scan(Path.Combine(folder, "nueva")).Count == 2, "Publishing copies a family into the library, creating the folder, and never overwrites one with the same name");
        check(Throws(() => FamilyLibrary.Publish(Path.Combine(library, "dañado.g3df"), library)) && Throws(() => FamilyLibrary.Publish(Path.Combine(library, "notas.txt"), library)) && Throws(() => FamilyLibrary.Publish(Path.Combine(folder, "nada.g3df"), library)) &&
              Throws(() => FamilyLibrary.Publish(published, " ")), "A damaged file, another kind of file, a missing file and no folder are refused");
    }

    private static byte[] ReadAllBytes(this Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
