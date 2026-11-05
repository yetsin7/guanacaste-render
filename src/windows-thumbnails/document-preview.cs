using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;

namespace Guanacaste3D.Thumbnails;

/// <summary>Produces portable, text-free drawings from verified document content without loading external assets.</summary>
public static class DocumentPreview
{
    private readonly record struct PreviewLine(Point Start, Point End);

    /// <summary>Renders native containers and earlier signed family/template files on an STA thread.</summary>
    public static BitmapSource Render(byte[] snapshot, int size)
    {
        if (size is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        var first = Array.FindIndex(snapshot, value => value is not (9 or 10 or 13 or 32));
        if (first >= 0 && snapshot[first] == (byte)'{')
        {
            using var exchange = JsonDocument.Parse(snapshot);
            return ExchangePreview.ExchangeThumbnail.Render(exchange.RootElement, size);
        }
        IReadOnlyList<PreviewLine> lines;
        if (snapshot.AsSpan().StartsWith("G3DNATV1"u8))
        {
            using var stream = new MemoryStream(snapshot, writable: false);
            var (info, json) = NativeDocumentStore.ReadPayload(stream);
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.GetProperty("Id").GetGuid() != info.DocumentId ||
                parsed.RootElement.GetProperty("SchemaVersion").GetInt32() != info.SchemaVersion)
                throw new InvalidDataException("The thumbnail header and payload identity do not match.");
            lines = info.Kind switch
            {
                NativeDocumentKind.Project => ProjectLines(ProjectFile.DeserializeDocument(Encoding.UTF8.GetString(json))),
                NativeDocumentKind.Family => FamilyLines(JsonSerializer.Deserialize<FamilyDocument>(json)!),
                NativeDocumentKind.Template => TemplateLines(JsonSerializer.Deserialize<ProjectTemplateDocument>(json)!),
                _ => throw new InvalidDataException("Unsupported thumbnail document kind.")
            };
        }
        else
        {
            if (snapshot.Length > NativeContainer.MaximumBytes) throw new InvalidDataException("The legacy thumbnail input is too large.");
            var payload = NativeContainer.Open(Encoding.UTF8.GetString(snapshot));
            if (payload.Signature == NativeContainer.FamilySignature && payload.SchemaVersion == 1)
            {
                var family = JsonSerializer.Deserialize<OpeningFamilyDocument>(payload.Json)!;
                family.Validate();
                lines = OpeningLines(family.Types[0].Width, family.Types[0].Height);
            }
            else if (payload.Signature == NativeContainer.TemplateSignature)
                lines = LevelLines(ProjectFile.DeserializeDocument(payload.Json).Levels.Select(level => level.Elevation));
            else throw new InvalidDataException("Unsupported legacy thumbnail signature.");
        }
        if (lines.Count == 0) throw new InvalidDataException("The document has no previewable geometry.");
        return Draw(lines, size);
    }

    /// <summary>Projects the actual model while excluding the ground plane that would obscure its scale.</summary>
    private static IReadOnlyList<PreviewLine> ProjectLines(BuildingDocument document)
    {
        var mesh = BuildingMesher.Build(document);
        var indices = new List<int>();
        for (var triangle = 0; triangle < mesh.TriangleCount; triangle++)
            if (mesh.TriangleKinds[triangle] != SurfaceKind.Ground)
                indices.AddRange([mesh.Indices[triangle * 3], mesh.Indices[triangle * 3 + 1], mesh.Indices[triangle * 3 + 2]]);
        return indices.Count == 0 ? LevelLines(document.Levels.Select(level => level.Elevation)) : MeshLines(mesh.Positions, indices);
    }

    /// <summary>Uses mesh geometry, editable paper graphics, or regenerated family solids according to the stored family kind.</summary>
    private static IReadOnlyList<PreviewLine> FamilyLines(FamilyDocument family)
    {
        family.Validate();
        if (family.SheetLayout is { } layout)
        {
            var lines = Rectangle(0, 0, layout.Width, layout.Height).ToList();
            foreach (var graphic in layout.Graphics)
            {
                if (graphic.Kind == SheetGraphicKind.Line)
                    lines.Add(new(new(graphic.Start.X, graphic.Start.Y), new(graphic.End.X, graphic.End.Y)));
                else if (graphic.Kind == SheetGraphicKind.Rectangle)
                    lines.AddRange(Rectangle(graphic.Start.X, graphic.Start.Y, graphic.End.X - graphic.Start.X, graphic.End.Y - graphic.Start.Y));
            }
            return lines;
        }
        if (family.Mesh is { } familyMesh)
        {
            var decoded = (familyMesh.Preview ?? familyMesh).Decode();
            return MeshLines(decoded.Positions, decoded.Indices);
        }
        var mesh = FamilyGeometry.Tessellate(family);
        return mesh.TriangleCount > 0 ? MeshLines(mesh.Positions, mesh.Indices) : OperationLines(family);
    }

    /// <summary>Shows an operation-tree diagram for definitions that do not yet contain tessellatable solids.</summary>
    private static IReadOnlyList<PreviewLine> OperationLines(FamilyDocument family)
    {
        var lines = Rectangle(0, 0, 2, 1).ToList();
        var count = Math.Clamp(family.Operations.Count, 1, 8);
        lines.Add(new(new(1, 1), new(1, count * 1.5 + 0.5)));
        for (var index = 0; index < count; index++)
        {
            var y = 1.5 + index * 1.5;
            lines.Add(new(new(1, y + 0.5), new(2, y + 0.5)));
            lines.AddRange(Rectangle(2, y, 2, 1));
        }
        return lines;
    }

    /// <summary>Shows the template's level configuration because templates intentionally contain no model geometry.</summary>
    private static IReadOnlyList<PreviewLine> TemplateLines(ProjectTemplateDocument template)
    {
        template.Validate();
        return LevelLines(template.Levels.Select(level => level.Elevation));
    }

    /// <summary>Draws real level elevations with datum circles; no arbitrary building is substituted for a template.</summary>
    private static IReadOnlyList<PreviewLine> LevelLines(IEnumerable<double> elevations)
    {
        var levels = elevations.Distinct().Order().ToArray();
        if (levels.Length == 0) return [];
        var width = Math.Max(3, levels[^1] - levels[0]);
        var lines = new List<PreviewLine>();
        foreach (var elevation in levels)
        {
            lines.Add(new(new(0, -elevation), new(width, -elevation)));
            var radius = width * 0.035;
            for (var segment = 0; segment < 16; segment++)
            {
                var angle = segment * Math.PI / 8;
                var next = (segment + 1) * Math.PI / 8;
                lines.Add(new(new(width + radius + radius * Math.Cos(angle), -elevation + radius * Math.Sin(angle)),
                    new(width + radius + radius * Math.Cos(next), -elevation + radius * Math.Sin(next))));
            }
        }
        return lines;
    }

    /// <summary>Shows the dimensions of the first legacy opening type and its inset frame.</summary>
    private static IReadOnlyList<PreviewLine> OpeningLines(double width, double height) =>
        Rectangle(0, 0, width, height).Concat(Rectangle(width * 0.06, height * 0.04, width * 0.88, height * 0.92)).ToArray();

    /// <summary>Creates four edges for paper borders and legacy opening frames.</summary>
    private static IEnumerable<PreviewLine> Rectangle(double x, double y, double width, double height) =>
    [
        new(new(x, y), new(x + width, y)), new(new(x + width, y), new(x + width, y + height)),
        new(new(x + width, y + height), new(x, y + height)), new(new(x, y + height), new(x, y))
    ];

    /// <summary>Extracts a bounded set of feature edges instead of drawing every triangulation seam.</summary>
    private static IReadOnlyList<PreviewLine> MeshLines(IReadOnlyList<Vector3> positions, IReadOnlyList<int> indices) =>
        FeatureEdges.Extract(positions, indices, maximumEdges: 26000)
            .Select(edge => new PreviewLine(Project(edge.A), Project(edge.B))).ToArray();

    /// <summary>Uses a fixed isometric camera so previews remain recognizable across saves.</summary>
    private static Point Project(Vector3 point) =>
        new((point.X - point.Y) * 0.70710678, -((point.X + point.Y) * 0.40824829 + point.Z * 0.81649658));

    /// <summary>Fits the drawing to an opaque square at the exact Shell-requested resolution.</summary>
    private static BitmapSource Draw(IReadOnlyList<PreviewLine> lines, int size)
    {
        var points = lines.SelectMany(line => new[] { line.Start, line.End }).ToArray();
        var minimumX = points.Min(point => point.X);
        var minimumY = points.Min(point => point.Y);
        var maximumX = points.Max(point => point.X);
        var maximumY = points.Max(point => point.Y);
        var scale = size * 0.86 / Math.Max(0.001, Math.Max(maximumX - minimumX, maximumY - minimumY));
        var center = new Point((minimumX + maximumX) / 2, (minimumY + maximumY) / 2);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, size, size));
            var pen = new Pen(Brushes.DarkSlateGray, Math.Max(0.65, size / 400d));
            foreach (var line in lines)
                context.DrawLine(pen, new Point(size / 2d + (line.Start.X - center.X) * scale, size / 2d + (line.Start.Y - center.Y) * scale),
                    new Point(size / 2d + (line.End.X - center.X) * scale, size / 2d + (line.End.Y - center.Y) * scale));
        }
        var image = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }
}
