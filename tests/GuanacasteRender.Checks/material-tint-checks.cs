using System.Numerics;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

/// <summary>Checks Lumion-style colorización of render materials: parsing, validation, tinting and persistence.</summary>
static class MaterialTintChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(MaterialTint.TryParse("#FFFFFF", out var white) && white == Vector3.One &&
              MaterialTint.TryParse("#000000", out var black) && black == Vector3.Zero,
            "A tint reads white and black from their hex text");
        check(MaterialTint.TryParse("#0A80FF", out var decoded) &&
              MathF.Abs(decoded.X - 0.00303527f) < 0.00001f &&
              MathF.Abs(decoded.Y - 0.2158605f) < 0.00001f &&
              decoded.Z == 1f,
            "Tint channels use the exact sRGB transfer function, including its dark linear segment");
        check(MaterialTint.TryParse("#040000", out var nearBlack) &&
              MathF.Abs(nearBlack.X - (4f / 255f / 12.92f)) < 0.000001f,
            "Near-black tint values do not use the inaccurate gamma-2.2 approximation");

        check(!MaterialTint.TryParse("rojo", out _) && !MaterialTint.TryParse("#FFF", out _) && !MaterialTint.TryParse("#GG0000", out _) &&
              !MaterialTint.TryParse(null, out _) && MaterialTint.IsValid(null) && !MaterialTint.IsValid("#12"),
            "Malformed tint text never tints and is rejected by validation");

        var brick = new RenderMaterial { Name = "Ladrillo", BaseColor = new Vector3(0.8f, 0.4f, 0.2f) };
        var tinted = MaterialTint.Apply(brick, "#808080");
        check(tinted.BaseColor.X < brick.BaseColor.X && tinted.BaseColor.Y < brick.BaseColor.Y && tinted.BaseColor.Z < brick.BaseColor.Z &&
              MaterialTint.Apply(brick, null) == brick,
            "A tint darkens the base colour it multiplies and no tint leaves the material alone");

        var session = new DocumentSession(BuildingDocument.Create("Tint"));
        session.SetRenderMaterial(new RenderMaterialAssignment(SurfaceKind.Wall, "stock:") { TintHex = "#B5651D", TextureSize = 2.5 });
        var path = Path.Combine(Path.GetTempPath(), $"tint-{Guid.NewGuid():N}.g3d");
        try
        {
            ProjectFile.Save(path, session.Document);
            var saved = ProjectFile.Load(path).RenderMaterials.Single();
            check(saved.TintHex == "#B5651D" && saved.TextureSize == 2.5, "A material's tint and map scale survive save and reopen");
        }
        finally { File.Delete(path); }

        var rejected = false;
        try { session.SetRenderMaterial(new RenderMaterialAssignment(SurfaceKind.Floor, "stock:") { TintHex = "azul" }); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "A material with a malformed tint is refused");
    }
}
