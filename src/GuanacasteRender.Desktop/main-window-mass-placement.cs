using System.IO;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Mass placement, as in Lumion: choose a family and how to spread it, then sketch a line or an area in plan; the
/// instances are added in one undoable step, loading a shipped family into the project first when needed.
/// </summary>
public partial class MainWindow
{
    private void BeginMassPlacement()
    {
        var families = MassPlacementDialog.Families(_session.Document, Path.Combine(AppContext.BaseDirectory, "resources", "families"));
        if (families.Count == 0)
        {
            StatusText.Text = GetText("Status.MassPlacementNoFamilies");
            return;
        }
        var dialog = new MassPlacementDialog(families) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } request) return;
        ViewTabs.SelectedIndex = 0;
        BeginSlabSketch(ArchitecturalLibrary.SlabPresets.First(), false);
        _slabSketch = _slabSketch! with { Open = !request.Area, Complete = points => FinishMassPlacement(request, points) };
        StatusText.Text = GetText(request.Area ? "Status.MassPlacementArea" : "Status.MassPlacementLine");
    }

    private void FinishMassPlacement(MassPlacementRequest request, IReadOnlyList<Point2> points)
    {
        try
        {
            var placements = request.Area ? MassPlacement.InArea(points, request.Options) : MassPlacement.AlongPath(points, request.Options);
            // Loading a library family and placing its instances undo together.
            using var group = _session.BeginGroup("Transaction.MassPlaceComponents");
            var familyId = request.Family.LoadedId ??
                           _session.LoadComponentFamily(NativeDocumentStore.LoadFamily(request.Family.LibraryPath!));
            var placed = _session.PlaceComponentFamilies(_activeLevelId, familyId, placements);
            group.Assimilate();
            StatusText.Text = FormatText("Status.MassPlacementDone", placed.Count, request.Family.Label);
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        RefreshAll();
    }
}
