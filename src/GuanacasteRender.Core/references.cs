namespace Guanacaste3D.Core;

/// <summary>What kind of file a project keeps inside itself as a reference.</summary>
public enum ReferenceKind { Image, Pdf }

/// <summary>An image or a PDF kept inside the project, so the project can be moved without it getting lost.</summary>
public sealed record ReferenceFile(Guid Id, ReferenceKind Kind, string Name, byte[] Data, int PixelWidth, int PixelHeight);

/// <summary>An image placed in the plan of a level, as a drawing to trace over: its lower-left corner, its width in meters and its turn.</summary>
public sealed record ImageUnderlay(Guid Id, Guid LevelId, Guid ReferenceId, Point2 Origin, double Width, double RotationDegrees, double Opacity)
{
    public bool IsValid => Id != Guid.Empty && Origin.X is > -1e7 and < 1e7 && Origin.Y is > -1e7 and < 1e7 && double.IsFinite(Origin.X + Origin.Y) &&
        Width is >= 0.1 and <= 10000 && double.IsFinite(RotationDegrees) && Math.Abs(RotationDegrees) <= 360 && Opacity is >= 0.05 and <= 1;
}

public static class ReferenceFiles
{
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    public const int MaximumTotalBytes = 12 * 1024 * 1024;
    public const int MaximumFiles = 60;

    /// <summary>Whether the bytes begin like a PNG, JPEG, BMP or GIF image.</summary>
    public static bool LooksLikeImage(byte[] data) => data.Length > 8 &&
        (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 || data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF ||
         data[0] == (byte)'B' && data[1] == (byte)'M' || data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8');

    /// <summary>Whether the bytes begin like a PDF document.</summary>
    public static bool LooksLikePdf(byte[] data) => data.Length > 8 && data[0] == (byte)'%' && data[1] == (byte)'P' && data[2] == (byte)'D' && data[3] == (byte)'F' && data[4] == (byte)'-';

    public static void Validate(BuildingDocument document)
    {
        if (document.References is null || document.Underlays is null || document.PointClouds is null || document.References.Count > MaximumFiles ||
            document.Underlays.Count > 500 || document.PointClouds.Count > 20 ||
            document.References.Any(file => file is null || file.Id == Guid.Empty || string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 160 || !Enum.IsDefined(file.Kind) ||
                file.Data is null || file.Data.Length is 0 or > MaximumFileBytes || (file.Kind == ReferenceKind.Image && (file.PixelWidth < 1 || file.PixelHeight < 1 || !LooksLikeImage(file.Data))) ||
                (file.Kind == ReferenceKind.Pdf && !LooksLikePdf(file.Data))) ||
            document.References.Sum(file => (long)file.Data.Length) > MaximumTotalBytes ||
            document.References.Select(file => file.Id).Distinct().Count() != document.References.Count ||
            document.Underlays.Any(item => item is null || !item.IsValid || document.References.All(file => file.Id != item.ReferenceId || file.Kind != ReferenceKind.Image) ||
                document.Levels.All(level => level.Id != item.LevelId)) ||
            document.PointClouds.Any(cloud => cloud is null || cloud.Id == Guid.Empty || cloud.Points is null || cloud.Points.Count is < 1 or > SurveyPointReader.MaximumCloudPoints ||
                document.Levels.All(level => level.Id != cloud.LevelId)))
            throw new InvalidDataException("Invalid reference files.");
    }
}

public sealed partial class DocumentSession
{
    /// <summary>Keeps an image or a PDF inside the project. Images need their size in pixels; the file must be what it says it is.</summary>
    public Guid AddReferenceFile(ReferenceKind kind, string name, byte[] data, int pixelWidth = 0, int pixelHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(name) || name.Length > 160) throw new InvalidDataException("The name is empty or too long.");
        if (data.Length == 0 || data.Length > ReferenceFiles.MaximumFileBytes) throw new InvalidDataException("The file is too large to keep in the project.");
        if (kind == ReferenceKind.Image ? !ReferenceFiles.LooksLikeImage(data) || pixelWidth < 1 || pixelHeight < 1 : !ReferenceFiles.LooksLikePdf(data))
            throw new InvalidDataException("The file is not what it says it is.");
        if (Document.References.Count >= ReferenceFiles.MaximumFiles || Document.References.Sum(file => (long)file.Data.Length) + data.Length > ReferenceFiles.MaximumTotalBytes)
            throw new InvalidDataException("The project already keeps too many reference files.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddReferenceFile", document => document.References.Add(new ReferenceFile(id, kind, name.Trim(), data, kind == ReferenceKind.Image ? pixelWidth : 0, kind == ReferenceKind.Image ? pixelHeight : 0)));
        return id;
    }

    public void RemoveReferenceFile(Guid id)
    {
        if (Document.References.All(file => file.Id != id)) throw new InvalidDataException("The reference file does not exist.");
        Commit("Transaction.RemoveReferenceFile", document =>
        {
            document.References.RemoveAll(file => file.Id == id);
            document.Underlays.RemoveAll(item => item.ReferenceId == id);
        });
    }

    /// <summary>Places a kept image in the plan of a level, <paramref name="width"/> meters wide.</summary>
    public Guid PlaceImageUnderlay(Guid levelId, Guid referenceId, Point2 origin, double width)
    {
        if (Document.References.All(file => file.Id != referenceId || file.Kind != ReferenceKind.Image)) throw new InvalidDataException("The reference file does not exist.");
        var underlay = new ImageUnderlay(Guid.NewGuid(), levelId, referenceId, origin, width, 0, 0.6);
        if (!underlay.IsValid) throw new InvalidDataException("The image placement is invalid.");
        if (Document.Levels.All(level => level.Id != levelId)) throw new InvalidDataException("The level does not exist.");
        Commit("Transaction.PlaceImageUnderlay", document => document.Underlays.Add(underlay));
        return underlay.Id;
    }

    /// <summary>Changes where an image lies in the plan, its width, its turn and how see-through it is.</summary>
    public void SetImageUnderlay(Guid id, Point2 origin, double width, double rotationDegrees, double opacity)
    {
        var current = Document.Underlays.FirstOrDefault(item => item.Id == id) ?? throw new InvalidDataException("The image does not exist.");
        var changed = current with { Origin = origin, Width = width, RotationDegrees = rotationDegrees, Opacity = opacity };
        if (!changed.IsValid) throw new InvalidDataException("The image placement is invalid.");
        Commit("Transaction.SetImageUnderlay", document =>
        {
            var index = document.Underlays.FindIndex(item => item.Id == id);
            document.Underlays[index] = changed;
        });
    }

    public void RemoveImageUnderlay(Guid id)
    {
        if (Document.Underlays.All(item => item.Id != id)) throw new InvalidDataException("The image does not exist.");
        Commit("Transaction.RemoveImageUnderlay", document => document.Underlays.RemoveAll(item => item.Id == id));
    }
}
