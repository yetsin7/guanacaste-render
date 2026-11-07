using System.Reflection;

namespace Guanacaste3D.Core;

/// <summary>A part of the building that one person can take for editing and others cannot, and that can be hidden as a whole.</summary>
public sealed record Workset(Guid Id, string Name, string? Owner, bool Visible);

/// <summary>Puts an element in a workset. The <c>Id</c> is the element's own identifier, so an element is in one workset.</summary>
public sealed record WorksetAssignment(Guid Id, Guid WorksetId);

/// <summary>Whether the project is shared through a central model, who is working on this copy and when it last synchronized.</summary>
public sealed record CollaborationSettings(bool Enabled, string User, string CentralPath, DateTime? LastSyncUtc)
{
    public static CollaborationSettings Off { get; } = new(false, string.Empty, string.Empty, null);
    public bool IsValid => (!Enabled || (!string.IsNullOrWhiteSpace(User) && User.Length <= 60 && !string.IsNullOrWhiteSpace(CentralPath) && CentralPath.Length <= 500)) &&
                           User is not null && CentralPath is not null && User.Length <= 60 && CentralPath.Length <= 500;
}

/// <summary>What a synchronization did.</summary>
public sealed record SyncReport(bool CreatedCentral, bool WroteCentral, MergeReport? Merge, DateTime WhenUtc);

/// <summary>The state of the shared model as seen from this copy.</summary>
public sealed record CollaborationStatus(bool Enabled, string User, string CentralPath, bool CentralExists, DateTime? LastSyncUtc, bool CentralChangedSince,
    int LocalAdded, int LocalChanged, int LocalRemoved, IReadOnlyList<Workset> Worksets);

public static class CentralModel
{
    /// <summary>Where the copy of the central model this project last synchronized with is kept, per project and per person.</summary>
    public static string BasePath(Guid projectId, string user)
    {
        var folder = Environment.GetEnvironmentVariable("G3D_CENTRAL_DIR") is { Length: > 0 } custom
            ? custom : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guanacaste 3D", "Central");
        Directory.CreateDirectory(folder);
        var safe = new string(user.Select(letter => char.IsLetterOrDigit(letter) ? letter : '_').ToArray());
        return Path.Combine(folder, $"{projectId:N}-{safe}.base.g3d");
    }
}

public sealed partial class DocumentSession
{
    private bool _bypassWorksetCheck;

    /// <summary>The last synchronization of this session, or null if there has not been one.</summary>
    public SyncReport? LastSync { get; private set; }

    private bool IsOther(string? owner) => owner is not null && !string.Equals(owner, Document.Collaboration.User, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Refuses a change that touches an element of a workset another person has taken.</summary>
    private void EnforceWorksetOwnership(BuildingDocument before, BuildingDocument after)
    {
        if (_bypassWorksetCheck || !before.Collaboration.Enabled || !before.Worksets.Any(workset => IsOther(workset.Owner))) return;
        var foreign = before.WorksetAssignments.Where(item => before.Worksets.FirstOrDefault(workset => workset.Id == item.WorksetId) is { } owner && IsOther(owner.Owner))
            .Select(item => item.Id).ToHashSet();
        if (foreign.Count == 0) return;
        foreach (var (collection, change) in ModelMerge.Differences(before, after))
        {
            if (collection is nameof(BuildingDocument.Worksets) or nameof(BuildingDocument.WorksetAssignments)) continue;
            if (change.Changed.Concat(change.Removed).Any(foreign.Contains))
                throw new InvalidDataException("The element belongs to a workset that another person has taken.");
        }
    }

    // ------------------------------------------------------------------ worksets

    public void EnableCollaboration(string user, string centralPath)
    {
        var settings = new CollaborationSettings(true, user?.Trim() ?? string.Empty, centralPath?.Trim() ?? string.Empty, null);
        if (!settings.IsValid) throw new InvalidDataException("The collaboration settings are invalid.");
        Commit("Transaction.EnableCollaboration", document => document.Collaboration = settings);
        RecentCentralModels.Add(settings.CentralPath);
    }

    public void DisableCollaboration() => Commit("Transaction.DisableCollaboration", document => document.Collaboration = CollaborationSettings.Off);

    public Guid AddWorkset(string name)
    {
        var clean = name?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean.Length > 120) throw new InvalidDataException("The name is empty or too long.");
        if (Document.Worksets.Any(item => item.Name.Equals(clean, StringComparison.CurrentCultureIgnoreCase))) throw new InvalidDataException("A workset with that name already exists.");
        var id = Guid.NewGuid();
        Commit("Transaction.AddWorkset", document => document.Worksets.Add(new Workset(id, clean, null, true)));
        return id;
    }

    public void RemoveWorkset(Guid id)
    {
        var workset = Document.Worksets.FirstOrDefault(item => item.Id == id) ?? throw new InvalidDataException("The workset does not exist.");
        if (IsOther(workset.Owner)) throw new InvalidDataException("The workset has been taken by another person.");
        Commit("Transaction.RemoveWorkset", document =>
        {
            document.Worksets.RemoveAll(item => item.Id == id);
            document.WorksetAssignments.RemoveAll(item => item.WorksetId == id);
        });
    }

    /// <summary>Puts elements in a workset, or (null) takes them out of any. Elements of a workset another person has taken cannot be moved.</summary>
    public void AssignToWorkset(IReadOnlyCollection<Guid> elementIds, Guid? worksetId)
    {
        ArgumentNullException.ThrowIfNull(elementIds);
        if (elementIds.Count == 0 || elementIds.Any(id => !ElementGroups.Exists(Document, id))) throw new InvalidDataException("The element does not exist.");
        if (worksetId is { } wanted)
        {
            var target = Document.Worksets.FirstOrDefault(item => item.Id == wanted) ?? throw new InvalidDataException("The workset does not exist.");
            if (IsOther(target.Owner)) throw new InvalidDataException("The workset has been taken by another person.");
        }
        var ids = elementIds.Distinct().ToArray();
        if (Document.WorksetAssignments.Any(item => ids.Contains(item.Id) && Document.Worksets.FirstOrDefault(w => w.Id == item.WorksetId) is { } current && IsOther(current.Owner)))
            throw new InvalidDataException("The workset has been taken by another person.");
        Commit("Transaction.AssignToWorkset", document =>
        {
            document.WorksetAssignments.RemoveAll(item => ids.Contains(item.Id));
            if (worksetId is { } id) foreach (var element in ids) document.WorksetAssignments.Add(new WorksetAssignment(element, id));
        });
    }

    public void SetWorksetVisible(Guid id, bool visible)
    {
        if (Document.Worksets.All(item => item.Id != id)) throw new InvalidDataException("The workset does not exist.");
        Commit("Transaction.SetWorksetVisible", document =>
        {
            var index = document.Worksets.FindIndex(item => item.Id == id);
            document.Worksets[index] = document.Worksets[index] with { Visible = visible };
        });
    }

    /// <summary>Takes a workset for this person, so nobody else can change its elements once the claim reaches the central model.</summary>
    public void BorrowWorkset(Guid id)
    {
        if (!Document.Collaboration.Enabled) throw new InvalidDataException("Collaboration is not enabled.");
        var workset = Document.Worksets.FirstOrDefault(item => item.Id == id) ?? throw new InvalidDataException("The workset does not exist.");
        if (IsOther(workset.Owner)) throw new InvalidDataException("The workset has been taken by another person.");
        Commit("Transaction.BorrowWorkset", document =>
        {
            var index = document.Worksets.FindIndex(item => item.Id == id);
            document.Worksets[index] = document.Worksets[index] with { Owner = document.Collaboration.User };
        });
    }

    /// <summary>Lets go of every workset this person has taken. Returns how many.</summary>
    public int RelinquishWorksets()
    {
        var mine = Document.Worksets.Where(item => item.Owner is not null && !IsOther(item.Owner)).Select(item => item.Id).ToHashSet();
        if (mine.Count == 0) return 0;
        Commit("Transaction.RelinquishWorksets", document =>
        {
            for (var index = 0; index < document.Worksets.Count; index++)
                if (mine.Contains(document.Worksets[index].Id)) document.Worksets[index] = document.Worksets[index] with { Owner = null };
        });
        return mine.Count;
    }

    // ------------------------------------------------------------------ the central model

    private (BuildingDocument? Central, BuildingDocument Baseline) ReadCentral()
    {
        var settings = Document.Collaboration;
        if (!settings.Enabled) throw new InvalidDataException("Collaboration is not enabled.");
        var central = File.Exists(settings.CentralPath) ? ProjectFile.Load(settings.CentralPath) : null;
        var basePath = CentralModel.BasePath(Document.Id, settings.User);
        var baseline = File.Exists(basePath) ? ProjectFile.Load(basePath) : central ?? Document;
        return (central, baseline);
    }

    /// <summary>
    /// Brings this copy and the central model together: what others changed comes in, what this person changed goes
    /// out, and elements both changed keep this person's version unless a workset someone else took owns them.
    /// With <paramref name="writeCentral"/> false it only brings others' work in (Reload Latest).
    /// </summary>
    public SyncReport SynchronizeWithCentral(bool writeCentral = true)
    {
        var settings = Document.Collaboration;
        var (central, baseline) = ReadCentral();
        var now = DateTime.UtcNow;
        var basePath = CentralModel.BasePath(Document.Id, settings.User);
        if (central is null)
        {
            if (!writeCentral) throw new InvalidDataException("The central model does not exist yet.");
            var first = Document.Clone();
            first.Collaboration = settings with { LastSyncUtc = now };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settings.CentralPath))!);
            ProjectFile.Save(settings.CentralPath, first);
            ProjectFile.Save(basePath, first);
            Commit("Transaction.Synchronize", document => document.Collaboration = first.Collaboration);
            return LastSync = new SyncReport(true, true, null, now);
        }

        var result = ModelMerge.Merge(baseline, Document, central, (collection, id) =>
            collection == nameof(BuildingDocument.Worksets) ||
            (central.WorksetAssignments.FirstOrDefault(item => item.Id == id) is { } assignment &&
             central.Worksets.FirstOrDefault(w => w.Id == assignment.WorksetId) is { } workset && IsOther(workset.Owner)));
        var merged = result.Document;
        merged.Collaboration = settings with { LastSyncUtc = now };
        merged.Validate();
        _bypassWorksetCheck = true;
        try { Commit("Transaction.Synchronize", document => CopyProperties(merged, document)); }
        finally { _bypassWorksetCheck = false; }
        if (writeCentral)
        {
            ProjectFile.Save(settings.CentralPath, Document);
            ProjectFile.Save(basePath, Document);
        }
        return LastSync = new SyncReport(false, writeCentral, result.Report, now);
    }

    private static void CopyProperties(BuildingDocument from, BuildingDocument to)
    {
        foreach (var property in typeof(BuildingDocument).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.CanWrite && property.Name is not ("Id" or "SchemaVersion")) property.SetValue(to, property.GetValue(from));
    }

    /// <summary>How this copy stands against the central model: what it has changed since it last synchronized, and whether others have.</summary>
    public CollaborationStatus MonitorCollaboration()
    {
        var settings = Document.Collaboration;
        var central = settings.Enabled && File.Exists(settings.CentralPath);
        var basePath = CentralModel.BasePath(Document.Id, settings.User);
        int added = 0, changed = 0, removed = 0;
        var centralChanged = false;
        if (settings.Enabled && File.Exists(basePath))
        {
            foreach (var (_, change) in ModelMerge.Differences(ProjectFile.Load(basePath), Document))
            {
                added += change.Added.Count;
                changed += change.Changed.Count;
                removed += change.Removed.Count;
            }
            if (central) centralChanged = ModelMerge.Differences(ProjectFile.Load(basePath), ProjectFile.Load(settings.CentralPath)).Any();
        }
        return new CollaborationStatus(settings.Enabled, settings.User, settings.CentralPath, central, settings.LastSyncUtc, centralChanged, added, changed, removed, [.. Document.Worksets]);
    }
}
