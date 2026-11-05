namespace Guanacaste3D.Core;

/// <summary>Maintains the association between stair geometry and its generated railings.</summary>
public sealed partial class DocumentSession
{
    /// <summary>
    /// Rebuilds generated railing segments after a stair changes while retaining existing segment identities and styles
    /// where possible. Older projects without host references retain their independent railings.
    /// </summary>
    private static void RefreshHostedStairRailings(BuildingDocument document, StructuralElement stair)
    {
        if (stair.Kind != StructuralElementKind.Stair) return;
        var existing = document.StructuralElements.Where(element => element.RailingHostId == stair.Id).ToArray();
        if (existing.Length == 0) return;
        var lines = StairGeometry.GuardLines(stair);
        document.StructuralElements.RemoveAll(element => element.RailingHostId == stair.Id);
        for (var index = 0; index < lines.Count; index++)
        {
            var (start, end, offset, rise) = lines[index];
            var railing = index < existing.Length ? existing[index] :
                CreateStructuralElement(Guid.NewGuid(), document, stair.LevelId, StructuralElementKind.Railing,
                    "Guard", start, end) with { RailingHostId = stair.Id };
            document.StructuralElements.Add(railing with
            {
                LevelId = stair.LevelId, Start = start, End = end, Offset = offset, Rise = rise
            });
        }
    }
}
