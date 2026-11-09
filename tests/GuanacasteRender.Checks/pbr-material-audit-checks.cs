using Guanacaste3D.Core.Rendering;

internal static class PbrMaterialAuditChecks
{
    public static void Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "guanacaste-pbr-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "brick_albedo.png"), [137, 80, 78, 71, 13, 10, 26, 10]);
            File.WriteAllBytes(Path.Combine(folder, "brick_roughness.png"), [42, 73, 76]);
            File.WriteAllBytes(Path.Combine(folder, "brick_height.png"), [42, 73, 77]);
            var path = Path.Combine(folder, "brick.g3dm");
            File.WriteAllText(path, """
            {
              "version": 1,
              "name": "Ladrillo artesanal",
              "category": "Mampostería",
              "description": "Acabado exterior de ladrillo",
              "tags": ["exterior", "rojo"],
              "author": "Equipo Guanacaste",
              "license": "CC0",
              "albedo": "brick_albedo.png",
              "roughnessMap": "brick_roughness.png",
              "heightMap": "brick_height.png",
              "roughness": 0.85,
              "metallic": 0,
              "ior": 1.5,
              "textureSize": 0.4,
              "relief": 0.2
            }
            """);
            var valid = PbrMaterialAudit.Inspect(path);
            check(valid.IsValid && valid.Material?.Maps.Count == 3 &&
                  valid.Material.Maps.ContainsKey("heightMap") &&
                  !valid.Warnings.Any(w => w.Contains("heightMap", StringComparison.OrdinalIgnoreCase)) &&
                  valid.Material.Tags.Contains("exterior") && valid.Material.TextureSize == 0.4,
                "Portable PBR manifest retains real-world dimensions, categories and optional map metadata");

            File.WriteAllText(Path.Combine(folder, "wizard-v1.g3dm"),
                """{"Version":1,"Name":"From Windows Wizard","Albedo":"brick_albedo.png","Roughness":0.75,"TextureSize":1.2}""");
            var wizard = PbrMaterialAudit.Inspect(Path.Combine(folder, "wizard-v1.g3dm"));
            check(wizard.IsValid && wizard.Material?.Name == "From Windows Wizard" &&
                  wizard.Material.TextureSize == 1.2,
                "Existing PascalCase .g3dm wizard files remain readable");

            File.WriteAllText(Path.Combine(folder, "bad-traversal.g3dm"),
                """{"version":1,"name":"Unsafe","albedo":"../secret.png"}""");
            var traversal = PbrMaterialAudit.Inspect(Path.Combine(folder, "bad-traversal.g3dm"));
            check(!traversal.IsValid && traversal.Errors.Any(x => x.Contains("albedo", StringComparison.OrdinalIgnoreCase)),
                "PBR import rejects parent-directory traversal on every operating system");

            File.WriteAllText(Path.Combine(folder, "bad-height-traversal.g3dm"),
                """{"version":1,"name":"Unsafe height","albedo":"brick_albedo.png","heightMap":"../secret.png"}""");
            var heightTraversal = PbrMaterialAudit.Inspect(Path.Combine(folder, "bad-height-traversal.g3dm"));
            check(!heightTraversal.IsValid &&
                  heightTraversal.Errors.Any(x => x.Contains("heightMap", StringComparison.OrdinalIgnoreCase)),
                "Height maps use the same path-traversal protections as other PBR channels");

            File.WriteAllText(Path.Combine(folder, "bad-absolute.g3dm"),
                """{"version":1,"name":"Absolute","albedo":"/tmp/external.png"}""");
            check(!PbrMaterialAudit.Inspect(Path.Combine(folder, "bad-absolute.g3dm")).IsValid,
                "PBR import rejects absolute image paths");

            File.WriteAllText(Path.Combine(folder, "bad-physics.g3dm"),
                """{"version":1,"name":"Impossible","albedo":"brick_albedo.png","metallic":7}""");
            check(!PbrMaterialAudit.Inspect(Path.Combine(folder, "bad-physics.g3dm")).IsValid,
                "PBR import rejects invalid physical parameters");

            File.WriteAllText(Path.Combine(folder, "bad-missing.g3dm"),
                """{"version":1,"name":"Missing","albedo":"missing.png"}""");
            check(!PbrMaterialAudit.Inspect(Path.Combine(folder, "bad-missing.g3dm")).IsValid,
                "PBR import detects unavailable referenced textures");

            File.WriteAllText(Path.Combine(folder, "with-ao.g3dm"),
                """{"version":1,"name":"Future","albedo":"brick_albedo.png","ao":"occlusion.png"}""");
            var future = PbrMaterialAudit.Inspect(Path.Combine(folder, "with-ao.g3dm"));
            check(future.IsValid && future.Warnings.Any(w => w.Contains("ao", StringComparison.OrdinalIgnoreCase)),
                "Future PBR channels are reported without pretending the renderer supports them");

            File.WriteAllText(Path.Combine(folder, "duplicate-property.g3dm"),
                """{"version":1,"name":"Ambiguous","Albedo":"brick_albedo.png","albedo":"../secret.png"}""");
            check(!PbrMaterialAudit.Inspect(Path.Combine(folder, "duplicate-property.g3dm")).IsValid,
                "Case-insensitive duplicate JSON properties cannot bypass PBR validation");

            var scanned = PbrMaterialAudit.Scan(folder);
            check(scanned.Count == 9 && scanned.Count(entry => entry.IsValid) == 3 &&
                  scanned.Count(entry => !entry.IsValid) == 6,
                "Material catalog shows both valid materials and broken packages for repair");

            // Cross-platform case-sensitivity: detect a missing asset rather than guessing its filename.
            File.WriteAllText(Path.Combine(folder, "different-case.g3dm"),
                """{"version":1,"name":"Case mismatch","albedo":"BRICK_ALBEDO.PNG"}""");
            if (OperatingSystem.IsLinux())
                check(!PbrMaterialAudit.Inspect(Path.Combine(folder, "different-case.g3dm")).IsValid,
                    "Linux/macOS case-sensitive assets must preserve their filename spelling");

            var packedPath = Path.Combine(folder, "exported.g3dm");
            var exported = PbrMaterialPackageWriter.Create(packedPath,
                new PbrMaterialPackageWriter.Options("Repello fino", "Acabados",
                    "Con textura real a escala", "Guanacaste", "CC0",
                    .92, 0, 1.5, 1.25, .16, ["paredes", "interior"]),
                new Dictionary<string, string>
                {
                    ["albedo"] = Path.Combine(folder, "brick_albedo.png"),
                    ["roughnessMap"] = Path.Combine(folder, "brick_roughness.png"),
                    ["heightMap"] = Path.Combine(folder, "brick_height.png")
                });
            var exportedAudit = PbrMaterialAudit.Inspect(exported);
            check(exportedAudit.IsValid && exportedAudit.Material?.Maps.Count == 3 &&
                  exportedAudit.Material.Maps.ContainsKey("heightMap") &&
                  exportedAudit.Material.License == "CC0" &&
                  File.Exists(Path.Combine(folder, "exported-albedo.png")) &&
                  File.Exists(Path.Combine(folder, "exported-height.png")),
                "Portable writer creates a complete self-contained .g3dm material without WPF");

            var overwriteRejected = false;
            try
            {
                PbrMaterialPackageWriter.Create(packedPath,
                    new PbrMaterialPackageWriter.Options("No sobrescribir"),
                    new Dictionary<string, string> { ["albedo"] = Path.Combine(folder, "brick_albedo.png") });
            }
            catch (IOException) { overwriteRejected = true; }
            check(overwriteRejected &&
                  PbrMaterialAudit.Inspect(packedPath).Material?.Name == "Repello fino",
                "Exporting a material never silently overwrites an existing .g3dm package");

            check(PbrMaterialAudit.Scan(Path.Combine(folder, "not-a-folder")).Count == 0,
                "Missing material library directory is safe to inspect");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
