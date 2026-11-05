using System.Windows.Media;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Collects family sections for the active story, shared by the plan and sheet views.</summary>
    private IReadOnlyList<(Guid FamilyId, FamilyPlanSegment Segment)> ActiveFamilyPlanSegments()
    {
        var levels = _session.Document.Levels;
        var level = levels.Single(candidate => candidate.Id == _activeLevelId);
        var upperElevation = levels.Where(candidate => candidate.Elevation > level.Elevation)
            .Select(candidate => candidate.Elevation).DefaultIfEmpty(level.Elevation + 3).Min();
        return _session.Document.InPlaceFamilies.SelectMany(family =>
            FamilyPlanGeometry.ForLevel(family, level.Elevation, upperElevation)
                .Select(segment => (family.Id, segment))).ToArray();
    }

    /// <summary>Draws the same regenerated family geometry used by the 3D mesh in the plan canvas.</summary>
    private void RenderInPlaceFamilies()
    {
        foreach (var (familyId, segment) in ActiveFamilyPlanSegments())
            AddLine(segment.Start, segment.End, 1.4, Brushes.DarkSlateGray, familyId);
    }
}
