using System.IO;
using System.Text;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Loads the complete built-in project independently of loose installation files.</summary>
internal static class BreezeBlockSample
{
    /// <summary>Verifies the embedded native container before migrating and returning a fresh project.</summary>
    internal static BuildingDocument Load()
    {
        using var stream = typeof(BreezeBlockSample).Assembly.GetManifestResourceStream("Guanacaste3D.BreezeBlockHouse.g3d")
            ?? throw new InvalidDataException("The built-in sample project is missing.");
        var (info, json) = NativeDocumentStore.ReadPayload(stream);
        if (info.Kind != NativeDocumentKind.Project)
            throw new InvalidDataException("The built-in sample is not a project.");
        var document = ProjectFile.DeserializeDocument(Encoding.UTF8.GetString(json));
        if (document.Id != info.DocumentId)
            throw new InvalidDataException("The built-in sample project identity is invalid.");
        return document;
    }
}
