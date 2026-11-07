using System.IO;
using System.Text.Json;
using Guanacaste3D.ExchangePreview;

namespace Guanacaste3D.RevitBridge;

/// <summary>Embeds a lightweight preview without depending on an installed Guanacaste desktop application.</summary>
internal static class ExchangePreviewWriter
{
    /// <summary>Renders the exported geometry, preserves the version-1 payload, and atomically publishes the completed file.</summary>
    internal static void Complete(string temporary, string destination)
    {
        var previewPath = temporary + ".preview";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(temporary));
            byte[]? preview = null;
            try { preview = ExchangeThumbnail.EncodePng(ExchangeThumbnail.Render(document.RootElement, 512, useEmbedded: false)); }
            catch (Exception exception) when (exception is InvalidDataException or FormatException or ArgumentException or InvalidOperationException)
            {
                // Empty exports remain importable; the optional picture must not discard successfully exported BIM content.
            }
            using (var stream = File.Create(previewPath))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name is not ("thumbnailPng" or "exporterVersion")) property.WriteTo(writer);
                writer.WriteString("exporterVersion", "1.0.1");
                if (preview is not null) writer.WriteBase64String("thumbnailPng", preview);
                writer.WriteEndObject();
            }
            File.Move(previewPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(previewPath)) File.Delete(previewPath);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
