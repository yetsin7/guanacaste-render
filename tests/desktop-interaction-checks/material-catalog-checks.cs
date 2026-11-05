using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;
using Guanacaste3D.Desktop;

internal static partial class InteractionChecks
{
    /// <summary>Checks every shipped texture resolves, decodes, and has a localized label in the running application.</summary>
    private static void CheckBuiltInTextures()
    {
        var library = typeof(MainWindow).Assembly.GetType("Guanacaste3D.Desktop.RenderTextureLibrary")!;
        var catalog = (IEnumerable)library.GetProperty("BuiltIn", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var resolve = library.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)!;
        var load = library.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)!;
        var count = 0;
        var treeTextures = 0;
        foreach (var texture in catalog)
        {
            var textureType = texture.GetType();
            var key = (string)textureType.GetProperty("Key")!.GetValue(texture)!;
            var label = (string)textureType.GetProperty("LabelKey")!.GetValue(texture)!;
            var path = resolve.Invoke(null, ["builtin:" + key, null]);
            Check(path is string && load.Invoke(null, [path]) is not null &&
                  Application.Current.TryFindResource(label) is string,
                $"Built-in texture {key} is available, decodable and localized");
            if (key.StartsWith("tree-", StringComparison.Ordinal))
            {
                treeTextures++;
                var colorPath = path as string;
                var normalPath = colorPath is null ? null :
                    Path.Combine(Path.GetDirectoryName(colorPath)!, Path.GetFileNameWithoutExtension(colorPath) + "_NormalGL.png");
                Check(colorPath is not null && normalPath is not null && File.Exists(colorPath) && File.Exists(normalPath),
                    $"Nature material {key} generates a cached albedo and an OpenGL normal image");
            }
            count++;
        }
        Check(treeTextures == 24 && count >= 43,
            "The library includes twelve bark/trunk and twelve foliage textures in addition to the shipped photographic collection");
        Check(resolve.Invoke(null, ["builtin:../outside", null]) is null,
            "Unknown built-in texture identifiers cannot escape the texture catalog");
        // A missing library preset may remain in a legacy project. It is an
        // identifier, not a relative file path, and must safely use the stock finish.
        var projectFile = Path.Combine(Path.GetTempPath(), "legacy-material.g3d");
        Check(resolve.Invoke(null, ["material:removed-from-library", projectFile]) is null,
            "Unknown library material identifiers are never interpreted as filesystem paths");
        var missing = new[]
        {
            new RenderMaterialAssignment(SurfaceKind.Wall, "material:removed-from-library")
        };
        var missingResolver = (Func<SurfaceKind, RenderMaterial>)library.GetMethod("Resolver",
            BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [missing, projectFile, true])!;
        Check(missingResolver(SurfaceKind.Wall) == MaterialLibrary.ForKind(SurfaceKind.Wall),
            "Missing library materials fall back to the stock finish without crashing");
        // Regression: editing the size/relief of a library preset must affect the actual
        // ray-traced material, not merely the saved project values.
        var assignment = new RenderMaterialAssignment(SurfaceKind.Wall, MaterialPresets.Prefix + "oak-light")
        {
            TextureSize = 0.73,
            Relief = 0.64
        };
        var barkWithNormal = (Func<SurfaceKind, RenderMaterial>)library.GetMethod("Resolver",
            BindingFlags.Public | BindingFlags.Static)!.Invoke(null,
            [new[] { new RenderMaterialAssignment(SurfaceKind.Bark, "builtin:tree-bark-guanacaste")
            { TextureSize = 1.2, Relief = 0.6 } }, null, true])!;
        Check(barkWithNormal(SurfaceKind.Bark).Texture is not null &&
              barkWithNormal(SurfaceKind.Bark).NormalMap is not null,
            "Procedural tree bark loads its bundled OpenGL normal map as well as albedo");

        var assignments = new[] { assignment };
        var resolver = (Func<SurfaceKind, RenderMaterial>)library.GetMethod("Resolver",
            BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [assignments, null, true])!;
        var rendered = resolver(SurfaceKind.Wall);
        Check(rendered.Texture is not null &&
              Math.Abs(rendered.TextureSize - 0.73f) < 0.0001f &&
              Math.Abs(rendered.TextureRelief - 0.64f) < 0.0001f,
            "Preset material edits affect texture scale and relief in the renderer");

        // An external PBR bundle can ship alongside a project without embedding its raster images.
        var pbrDirectory = Path.Combine(Path.GetTempPath(), "guanacaste-pbr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pbrDirectory);
        try
        {
            var sourceImage = (string)resolve.Invoke(null, ["builtin:brick-red", null])!;
            File.Copy(sourceImage, Path.Combine(pbrDirectory, "albedo.png"));
            // The creation wizard uses the same packaging API. Assert real files are written
            // and that a new material can be immediately resolved by the renderer.
            var packageApi = typeof(MainWindow).Assembly.GetType("Guanacaste3D.Desktop.PbrMaterialPackages")!;
            var createPackage = packageApi.GetMethod("CreateFromImage",
                BindingFlags.Public | BindingFlags.Static)!;
            var createdPath = Path.Combine(pbrDirectory, "generated.g3dm");
            var generated = (string)createPackage.Invoke(null, [
                sourceImage, createdPath, "Concreto nuevo", 0.62, 0.0, 1.2, 0.18
            ])!;
            Check(generated == createdPath && File.Exists(createdPath) &&
                  File.Exists(Path.Combine(pbrDirectory, "generated-albedo.png")),
                "Create PBR from image writes a portable .g3dm manifest and albedo file");
            var generatedResolver = (Func<SurfaceKind, RenderMaterial>)library.GetMethod("Resolver",
                BindingFlags.Public | BindingFlags.Static)!.Invoke(null,
                [new[] { new RenderMaterialAssignment(SurfaceKind.Wall, "pbr:" + createdPath) {
                    TextureSize = 1.2, Relief = 0.18
                } }, null, true])!;
            Check(generatedResolver(SurfaceKind.Wall).Texture is not null &&
                  Math.Abs(generatedResolver(SurfaceKind.Wall).Roughness - 0.62f) < 0.0001f,
                "Newly created material is readable by the path tracer");
            File.Copy(sourceImage, Path.Combine(pbrDirectory, "normal.png"));
            File.Copy(sourceImage, Path.Combine(pbrDirectory, "roughness.png"));
            File.Copy(sourceImage, Path.Combine(pbrDirectory, "metallic.png"));
            File.Copy(sourceImage, Path.Combine(pbrDirectory, "height.png"));
            // Standard PBR sets should be detected and copied next to the native
            // .g3dm manifest, rather than being discarded by the image wizard.
            var standardColor = Path.Combine(pbrDirectory, "oak_basecolor.png");
            File.Copy(sourceImage, standardColor);
            foreach (var suffix in new[] { "_normal", "_roughness", "_metallic", "_height" })
                File.Copy(sourceImage, Path.Combine(pbrDirectory, "oak" + suffix + ".png"));
            var autoGenerated = (string)createPackage.Invoke(null, [
                standardColor, Path.Combine(pbrDirectory, "oak-set.g3dm"),
                "Roble PBR completo", 0.65, 0.0, 1.3, 0.2
            ])!;
            Check(File.Exists(Path.Combine(pbrDirectory, "oak-set-normal.png")) &&
                  File.Exists(Path.Combine(pbrDirectory, "oak-set-roughness.png")) &&
                  File.Exists(Path.Combine(pbrDirectory, "oak-set-metallic.png")) &&
                  File.Exists(Path.Combine(pbrDirectory, "oak-set-height.png")),
                "PBR creator copies normal, roughness, metallic and height companion maps");
            var autoResolver = (Func<SurfaceKind, RenderMaterial>)library.GetMethod("Resolver",
                BindingFlags.Public | BindingFlags.Static)!.Invoke(null,
                [new[] { new RenderMaterialAssignment(SurfaceKind.Wall, "pbr:" + autoGenerated)
                { TextureSize = 1.3, Relief = 0.2 } }, null, true])!;
            var autoMaterial = autoResolver(SurfaceKind.Wall);
            Check(autoMaterial.NormalMap is not null && autoMaterial.RoughnessMap is not null &&
                  autoMaterial.MetallicMap is not null && autoMaterial.HeightMap is not null,
                "Auto-generated .g3dm preserves four independent PBR detail channels");

            var manifestFile = Path.Combine(pbrDirectory, "test.g3dm");
            File.WriteAllText(manifestFile,
                "{\"version\":1,\"name\":\"Ladrillo PBR importado\",\"albedo\":\"albedo.png\"," +
                "\"roughness\":0.87,\"metallic\":0.05,\"textureSize\":0.4,\"relief\":0.2," +
                "\"normal\":\"normal.png\",\"roughnessMap\":\"roughness.png\",\"metallicMap\":\"metallic.png\"}");
            var pbrAssignments = new[] { new RenderMaterialAssignment(SurfaceKind.Wall, "pbr:" + manifestFile)
                { TextureSize = 0.4, Relief = 0.2 } };
            var pbrResolver = (Func<SurfaceKind, RenderMaterial>)library.GetMethod("Resolver",
                BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [pbrAssignments, null, true])!;
            var pbrMaterial = pbrResolver(SurfaceKind.Wall);
            Check(pbrMaterial.Texture is not null && pbrMaterial.Name == "Ladrillo PBR importado" &&
                  Math.Abs(pbrMaterial.Roughness - 0.87f) < 0.0001f &&
                  Math.Abs(pbrMaterial.Metallic - 0.05f) < 0.0001f &&
                  pbrMaterial.NormalMap is not null && pbrMaterial.RoughnessMap is not null &&
                  pbrMaterial.MetallicMap is not null,
                "Imported PBR bundle drives base color texture, roughness and metalness");
            File.WriteAllText(manifestFile,
                "{\"version\":1,\"name\":\"Traversal\",\"albedo\":\"../brick-red.png\"}");
            var rejectedResolver = (Func<SurfaceKind, RenderMaterial>)library.GetMethod("Resolver",
                BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [pbrAssignments, null, true])!;
            Check(rejectedResolver(SurfaceKind.Wall).Texture is null,
                "PBR manifest cannot reference images outside its directory");
        }
        finally { Directory.Delete(pbrDirectory, true); }

        // The ordinary WPF 3D viewport should show the same photographic texture.
        var previews = (IReadOnlyDictionary<SurfaceKind, (ImageSource Image, double Size)>)
            library.GetMethod("Previews3D", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [assignments, null])!;
        Check(previews.TryGetValue(SurfaceKind.Wall, out var preview) &&
              preview.Image is not null && Math.Abs(preview.Size - 0.73) < 1e-9,
            "A textured preset appears in the interactive 3D viewport at its edited scale");

        var plain = new[] { new RenderMaterialAssignment(SurfaceKind.Wall, MaterialPresets.Prefix + "paint-white") };
        var plainPreviews = (IReadOnlyDictionary<SurfaceKind, (ImageSource Image, double Size)>)
            library.GetMethod("Previews3D", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [plain, null])!;
        Check(!plainPreviews.ContainsKey(SurfaceKind.Wall),
            "Untextured presets do not resolve as nonexistent image files");
        // Gallery thumbnails are illuminated spheres rather than flat swatches.
        var sphereType = typeof(MainWindow).Assembly.GetType("Guanacaste3D.Desktop.MaterialSpherePreview")!;
        var sphereCreate = sphereType.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sphereSource = new BitmapImage(new Uri((string)resolve.Invoke(null, ["builtin:stone-paving", null])!));
        var sphere = (BitmapSource)sphereCreate.Invoke(null, [sphereSource, .85, 0.0, .6])!;
        Check(sphere.PixelWidth == 132 && sphere.PixelHeight == 84 && sphere.IsFrozen,
            "Material gallery generates a bounded, cross-thread-safe PBR sphere preview");
        var spherePixels = new byte[132 * 84 * 4];
        sphere.CopyPixels(spherePixels, 132 * 4, 0);
        var middle = (42 * 132 + 66) * 4;
        Check(spherePixels[middle] != spherePixels[0] ||
              spherePixels[middle + 1] != spherePixels[1] ||
              spherePixels[middle + 2] != spherePixels[2],
            "A shaded sphere is visibly distinct from the background");
        var renderPanel = typeof(MainWindow).Assembly.GetType("Guanacaste3D.Desktop.Controls.RenderPanel")!;
        var boundedDecode = renderPanel.GetMethod("DecodeGalleryImage", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tallImagePath = Path.Combine(Path.GetTempPath(), "guanacaste-tall-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            // An extremely narrow/tall texture must never decode at a huge height
            // simply because the thumbnail requests a fixed pixel width.
            var tallPixels = new byte[2 * 2048 * 4];
            var tallBitmap = BitmapSource.Create(2, 2048, 96, 96, PixelFormats.Bgra32, null, tallPixels, 8);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(tallBitmap));
            using (var output = File.Create(tallImagePath)) encoder.Save(output);
            var bounded = (BitmapSource)boundedDecode.Invoke(null, [tallImagePath, 192])!;
            Check(Math.Max(bounded.PixelWidth, bounded.PixelHeight) <= 192 &&
                  bounded.PixelHeight >= bounded.PixelWidth && bounded.IsFrozen,
                "Gallery decodes tall material images within the longest-edge budget");
        }
        finally { File.Delete(tallImagePath); }
        var resolveHdri = renderPanel.GetMethod("ResolveSavedHdri",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var file = Path.Combine(Path.GetTempPath(), "render-asset-test", "example.g3d");
        var expected = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "assets", "sky.hdr"));
        Check((string?)resolveHdri.Invoke(null, ["assets/sky.hdr", file]) == expected,
            "Project-relative HDRI references resolve in the project assets folder");
        Check(resolveHdri.Invoke(null, ["../other-project/secret.hdr", file]) is null &&
              resolveHdri.Invoke(null, [@"\\server\share\sky.hdr", file]) is null,
            "HDRI auto-restore rejects path traversal and UNC network references");

    }
}
