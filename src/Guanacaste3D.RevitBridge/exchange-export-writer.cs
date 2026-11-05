using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace Guanacaste3D.RevitBridge;

/// <summary>Persists exchange geometry with a portable preview before atomically replacing the destination.</summary>
internal sealed partial class ExchangeExporter
{
    /// <summary>Writes the current export and embeds a preview derived from exactly the exported element set.</summary>
    private void Write(string path, IReadOnlyList<Level> levels)
    {
        var temporary = path + ".partial";
        using (var stream = File.Create(temporary))
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "guanacaste-exchange");
            writer.WriteNumber("version", FormatVersion);
            writer.WriteString("source", "Revit " + _document.Application.VersionNumber);
            writer.WriteString("sourcePath", _document.PathName);
            // The file name, not Project Information: templates leave sample names such as "Snowdon Towers" there.
            writer.WriteString("projectName", _document.Title);
            writer.WriteString("units", "m");
            writer.WritePropertyName("levels");
            writer.WriteStartArray();
            foreach (var level in levels)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", level.Id.Value);
                writer.WriteString("name", level.Name);
                writer.WriteNumber("elevation", level.ProjectElevation * Meters);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("materials");
            new JsonArray([.. _materials.Select(material => (JsonNode)material)]).WriteTo(writer);
            writer.WritePropertyName("meshes");
            writer.WriteStartArray();
            foreach (var mesh in _meshes) mesh.WriteTo(writer);
            writer.WriteEndArray();
            writer.WritePropertyName("elements");
            _elements.WriteTo(writer);
            writer.WritePropertyName("skipped");
            writer.WriteStartObject();
            foreach (var (key, count) in _skipped) writer.WriteNumber(key, count);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        ExchangePreviewWriter.Complete(temporary, path);
    }
}
