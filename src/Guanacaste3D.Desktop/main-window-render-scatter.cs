using System.IO;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

/// <summary>What the Render tab's "Árboles y rocas" window needs from the project: the families to spread and a one-step placement.</summary>
public partial class MainWindow
{
    /// <summary>The families that can be spread: those loaded in the project, then the shipped trees, plants, rocks and cars.</summary>
    internal IReadOnlyList<MassPlacementFamily> ScatterFamilies() =>
        MassPlacementDialog.Families(_session.Document, Path.Combine(AppContext.BaseDirectory, "resources", "families"));

    /// <summary>
    /// Places the instances of a family on the level whose floor is the nearest at or below <paramref name="groundHeight"/>, as one
    /// undoable step with loading the family. Returns the sentence to show.
    /// </summary>
    internal string PlaceScattered(MassPlacementFamily family, IReadOnlyList<ComponentPlacement> placements, double groundHeight)
    {
        try
        {
            // On a toposolid they go on its level, so each stands on the slope; elsewhere on the level at or below the ground.
            var first = placements.Count > 0 ? placements[0].At : new Point2(0, 0);
            var level = new Core.Geometry.SiteTerrain(_session.Document).LevelFor(first.X, first.Y, groundHeight);
            if (level is null) return "El proyecto no tiene niveles donde poner nada.";
            using var group = _session.BeginGroup("Transaction.MassPlaceComponents");
            var familyId = family.LoadedId ?? _session.LoadComponentFamily(NativeDocumentStore.LoadFamily(family.LibraryPath!));
            var placed = _session.PlaceComponentFamilies(level.Id, familyId, placements);
            group.Assimilate();
            RefreshAll();
            return FormatText("Status.MassPlacementDone", placed.Count, family.Label);
        }
        catch (InvalidDataException exception) { return LocalizedErrors.Translate(exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return LocalizedErrors.Translate(exception.Message); }
    }
}
