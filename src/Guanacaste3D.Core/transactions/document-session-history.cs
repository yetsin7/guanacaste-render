namespace Guanacaste3D.Core;

/// <summary>One undoable step: the document state before the change and the change's name key.</summary>
public sealed record HistoryEntry(string Name, BuildingDocument Snapshot);

/// <summary>
/// Transaction and history mechanics. Every mutation is a named transaction:
/// it runs on a copy, regenerates dependent data, validates, and only then
/// replaces the active document. Names are localization keys
/// (for example "Transaction.AddWall") so the core stays UI-independent.
/// </summary>
public sealed partial class DocumentSession
{
    private const int MaxHistory = 40;
    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];
    private TransactionGroup? _openGroup;

    public bool CanUndo => _openGroup is null && _undo.Count > 0;
    public bool CanRedo => _openGroup is null && _redo.Count > 0;

    /// <summary>Name key of the transaction that Undo would revert, or null.</summary>
    public string? UndoName => CanUndo ? _undo[^1].Name : null;

    /// <summary>Name key of the transaction that Redo would reapply, or null.</summary>
    public string? RedoName => CanRedo ? _redo[^1].Name : null;

    /// <summary>Undo stack names, most recent first, for history lists and menus.</summary>
    public IReadOnlyList<string> UndoNames => _undo.Select(entry => entry.Name).Reverse().ToArray();

    /// <summary>Returns whether a transaction group is currently open.</summary>
    public bool IsGroupOpen => _openGroup is not null;

    /// <summary>Raised after every committed transaction, undo, redo, or group rollback.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Runs one named transaction atomically. Regeneration removes data that
    /// depends on deleted elements before validation, so either the whole
    /// change and its consequences commit or nothing does.
    /// </summary>
    private void Commit(string name, Action<BuildingDocument> action)
    {
        var copy = Document.Clone();
        action(copy);
        Regenerate(copy);
        copy.Validate();
        EnforceWorksetOwnership(Document, copy);
        PushSnapshot(_undo, new HistoryEntry(name, Document));
        _redo.Clear();
        Document = copy;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Recomputes dependent data after a mutation: instances follow their
    /// types first, then parameter values whose owners disappeared are pruned.
    /// Validation afterwards rejects any consequence that no longer fits.
    /// </summary>
    private static void Regenerate(BuildingDocument document)
    {
        document.Dimensions.RemoveAll(item => PlanDimensions.Resolve(document, item) is null);
        ElementGroups.Prune(document);
        for (var index = 0; index < document.Walls.Count; index++)
            if (document.Walls[index].BaseOffset > document.Walls[index].Height - 0.1)
                document.Walls[index] = document.Walls[index] with { BaseOffset = Math.Max(0, document.Walls[index].Height - 0.1) };
        document.Studies.RemoveAll(study => !document.StructuralElements.Any(item => item.Id == study.MassId && item.Kind == StructuralElementKind.Mass));
        foreach (var zone in document.Zones.ToArray())
        {
            var alive = zone.RoomIds.Where(id => document.Rooms.Any(room => room.Id == id)).ToList();
            if (alive.Count == 0) document.Zones.Remove(zone);
            else if (alive.Count != zone.RoomIds.Count) document.Zones[document.Zones.IndexOf(zone)] = zone with { RoomIds = alive };
        }
        document.CoverOverrides.RemoveAll(item => !ElementGroups.Exists(document, item.Id) && document.Columns.All(column => column.Id != item.Id));
        document.WorksetAssignments.RemoveAll(item => !ElementGroups.Exists(document, item.Id) || document.Worksets.All(workset => workset.Id != item.WorksetId));
        LevelConstraints.Apply(document);
        OpeningTypeRules.SynchronizeInstances(document);
        ParameterRules.RemoveOrphanedValues(document);
        ScheduleDefinitionRules.RemoveMissingFields(document);
    }

    /// <summary>Adds a history entry, trimming the oldest only when no group depends on the stack depth.</summary>
    private void PushSnapshot(List<HistoryEntry> list, HistoryEntry entry)
    {
        list.Add(entry);
        if (_openGroup is null) TrimHistory(list);
    }

    /// <summary>Enforces the bounded-memory history policy.</summary>
    private static void TrimHistory(List<HistoryEntry> list)
    {
        if (list.Count > MaxHistory) list.RemoveRange(0, list.Count - MaxHistory);
    }

    /// <summary>
    /// Opens a transaction group. Transactions committed while it is open are
    /// merged into one undo step by <see cref="TransactionGroup.Assimilate"/>,
    /// or reverted together by <see cref="TransactionGroup.RollBack"/>.
    /// Disposing an unfinished group rolls it back. Groups cannot be nested.
    /// </summary>
    public TransactionGroup BeginGroup(string name)
    {
        if (_openGroup is not null)
            throw new InvalidOperationException("A transaction group is already open.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A transaction group needs a name.", nameof(name));
        _openGroup = new TransactionGroup(this, name, Document, _undo.Count);
        return _openGroup;
    }

    /// <summary>Remembers where the history stands, so that everything done after can be made one undo step with <see cref="CollapseHistorySince"/>.</summary>
    public HistoryMark MarkHistory() => new(Document, _undo.Count);

    /// <summary>
    /// Merges every step taken since the mark into one named step, as a group does, but without holding a group open, so the steps may
    /// themselves be groups. Nothing changes when nothing was done since the mark, or when the history was trimmed past it.
    /// </summary>
    public void CollapseHistorySince(HistoryMark mark, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A step needs a name.", nameof(name));
        if (_openGroup is not null) throw new InvalidOperationException("A transaction group is open.");
        if (_undo.Count <= mark.UndoDepth || mark.UndoDepth < 0) return;
        _undo.RemoveRange(mark.UndoDepth, _undo.Count - mark.UndoDepth);
        _undo.Add(new HistoryEntry(name, mark.Start));
        TrimHistory(_undo);
    }

    /// <summary>Collapses the group's transactions into a single named history entry.</summary>
    internal void AssimilateGroup(TransactionGroup group)
    {
        EnsureOpen(group);
        _openGroup = null;
        if (_undo.Count > group.UndoDepth)
        {
            _undo.RemoveRange(group.UndoDepth, _undo.Count - group.UndoDepth);
            _undo.Add(new HistoryEntry(group.Name, group.StartDocument));
        }
        TrimHistory(_undo);
    }

    /// <summary>Restores the document to its state when the group began.</summary>
    internal void RollBackGroup(TransactionGroup group)
    {
        EnsureOpen(group);
        _openGroup = null;
        var changed = _undo.Count > group.UndoDepth;
        _undo.RemoveRange(group.UndoDepth, _undo.Count - group.UndoDepth);
        if (!changed) return;
        _redo.Clear();
        Document = group.StartDocument;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rejects operations on a group that is not the session's open group.</summary>
    private void EnsureOpen(TransactionGroup group)
    {
        if (!ReferenceEquals(_openGroup, group))
            throw new InvalidOperationException("The transaction group is not open.");
    }

    /// <summary>Restores the previous validated document snapshot when available.</summary>
    public void Undo()
    {
        if (!CanUndo) return;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        PushSnapshot(_redo, entry with { Snapshot = Document });
        Document = entry.Snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reapplies the next validated document snapshot when available.</summary>
    public void Redo()
    {
        if (!CanRedo) return;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        PushSnapshot(_undo, entry with { Snapshot = Document });
        Document = entry.Snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// A scope that aggregates several transactions into one undoable step. Use
/// with <c>using</c>: call <see cref="Assimilate"/> on success; leaving the
/// scope without it reverts every change made inside the group.
/// </summary>
public sealed class TransactionGroup : IDisposable
{
    private readonly DocumentSession _session;
    private bool _ended;

    internal TransactionGroup(DocumentSession session, string name, BuildingDocument start, int undoDepth)
    {
        _session = session;
        Name = name;
        StartDocument = start;
        UndoDepth = undoDepth;
    }

    /// <summary>Name key recorded as the single history entry.</summary>
    public string Name { get; }
    internal BuildingDocument StartDocument { get; }
    internal int UndoDepth { get; }

    /// <summary>Merges all inner transactions into one undo entry named after the group.</summary>
    public void Assimilate()
    {
        if (_ended) return;
        _ended = true;
        _session.AssimilateGroup(this);
    }

    /// <summary>Reverts every transaction committed inside the group.</summary>
    public void RollBack()
    {
        if (_ended) return;
        _ended = true;
        _session.RollBackGroup(this);
    }

    /// <summary>Rolls back when the group was neither assimilated nor rolled back.</summary>
    public void Dispose() => RollBack();
}

/// <summary>A point in the history: the document then and how many steps could be undone.</summary>
public readonly record struct HistoryMark(BuildingDocument Start, int UndoDepth);
