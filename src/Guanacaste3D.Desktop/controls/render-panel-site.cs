using System.Globalization;
using System.Windows;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// The project's place and orientation, as the sun of the render uses them. The latitude of the site fills the latitude box,
/// and the turn from true north is read every time the lighting is built, so a project turned away from north is lit on
/// the right side. A saved render setup keeps its own latitude; the site only moves it when the site changes afterwards.
/// </summary>
public partial class RenderPanel
{
    private (double Latitude, double TrueNorth)? _seenSite;

    private void RememberProjectSite()
    {
        if (_source is { } source) _seenSite = (source.Site.Latitude, source.Site.TrueNorthDegrees);
    }

    /// <summary>Follows a change of the project's site (the Coordinates window) and relights the preview.</summary>
    private void SyncProjectSite()
    {
        if (_source is null) return;
        var site = _source.Site;
        var seen = (site.Latitude, site.TrueNorthDegrees);
        if (_seenSite == seen) return;
        var latitudeMoved = _seenSite is not { } before || before.Latitude != site.Latitude;
        _seenSite = seen;
        if (latitudeMoved) LatitudeInput.Text = site.Latitude.ToString("0.###", CultureInfo.InvariantCulture);
        // The text box relights when its text changes; a turn from north changes nothing in it, so relight here as well.
        Lighting_Changed(this, new RoutedEventArgs());
    }
}
