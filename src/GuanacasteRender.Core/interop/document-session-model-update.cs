using Guanacaste3D.Core.Interop;

namespace Guanacaste3D.Core;

public sealed partial class DocumentSession
{
    /// <summary>
    /// Replaces the project with the result of a model update as one undoable step and stamps it with
    /// <paramref name="syncToken"/>, so undoing the update also restores which common model the project descends from.
    /// </summary>
    public void ApplyModelUpdate(BuildingDocument merged, string syncToken)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentException.ThrowIfNullOrWhiteSpace(syncToken);
        // The incoming model is authoritative for its elements regardless of who owns their worksets here.
        _bypassWorksetCheck = true;
        try
        {
            Commit("Transaction.UpdateModel", document =>
            {
                CopyProperties(merged, document);
                document.Extensions = new Dictionary<string, string>(merged.Extensions)
                {
                    [RenderModelUpdate.SyncTokenKey] = syncToken
                };
            });
        }
        finally { _bypassWorksetCheck = false; }
    }
}
