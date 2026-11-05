using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _coordinatesWindow;
    private Window? _positionWindow;

    /// <summary>Routes the Coordinates and Position buttons of the Manage tab.</summary>
    private bool TryExecuteSiteCommand(string commandId)
    {
        switch (commandId)
        {
            case "Coordinates": ShowCoordinatesWindow(); return true;
            case "Position": ShowPositionWindow(); return true;
            default: return false;
        }
    }

    private static bool TryNumber(string? text, out double value) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static string Plain(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private TextBox SiteField(string name, string value)
    {
        var box = new TextBox { Name = name, Text = value, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
        return box;
    }

    // ------------------------------------------------------------------ shared coordinates

    private void ShowCoordinatesWindow()
    {
        if (_coordinatesWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }
        var site = _session.Document.Site;
        var window = NewManageWindow("Site.Coordinates.Title", 460);
        window.Closed += (_, _) => _coordinatesWindow = null;
        _coordinatesWindow = window;

        var latitude = SiteField("SiteLatitude", Plain(site.Latitude));
        var longitude = SiteField("SiteLongitude", Plain(site.Longitude));
        var elevation = SiteField("SiteElevation", Plain(site.ElevationMeters));
        var east = SiteField("SiteEast", Plain(site.SharedEast));
        var north = SiteField("SiteNorth", Plain(site.SharedNorth));
        var projectPoint = SiteField("SiteProjectPoint", "0 0");
        var sharedPoint = SiteField("SiteSharedPoint", "0 0");
        var result = new TextBlock { Name = "SiteResult", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), MinHeight = 18 };

        bool Read(out ProjectSite value)
        {
            value = site;
            if (!TryNumber(latitude.Text, out var lat) || !TryNumber(longitude.Text, out var lon) || !TryNumber(elevation.Text, out var ele) ||
                !TryNumber(east.Text, out var e) || !TryNumber(north.Text, out var n)) return false;
            value = _session.Document.Site with { Latitude = lat, Longitude = lon, ElevationMeters = ele, SharedEast = e, SharedNorth = n };
            return value.IsValid;
        }
        bool ReadPair(string text, out double x, out double y)
        {
            x = y = 0;
            var parts = text.Split([(char)32, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length == 2 && TryNumber(parts[0], out x) && TryNumber(parts[1], out y);
        }

        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("SiteApply", GetText("Site.Apply"), (_, _) =>
        {
            if (!Read(out var value)) { StatusText.Text = GetText("Status.SiteInvalid"); return; }
            try
            {
                _session.SetProjectSite(value);
                StatusText.Text = GetText("Status.SiteChanged");
                RefreshAll();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("SiteToShared", GetText("Site.ToShared"), (_, _) =>
        {
            var parts = projectPoint.Text.Split([(char)32, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !Units.TryParseLength(parts[0], out var mx) || !Units.TryParseLength(parts[1], out var my) || !Read(out var value))
            {
                result.Text = GetText("Status.SiteInvalid");
                return;
            }
            var shared = value.ToShared(new Point2(mx, my));
            result.Text = FormatText("Site.SharedIs", shared.X, shared.Y);
        }));
        buttons.Children.Add(ManageButton("SiteToProject", GetText("Site.ToProject"), (_, _) =>
        {
            if (!ReadPair(sharedPoint.Text, out var x, out var y) || !Read(out var value)) { result.Text = GetText("Status.SiteInvalid"); return; }
            var point = value.ToProject(new Point2(x, y));
            result.Text = FormatText("Site.ProjectIs", Units.LengthNumber(point.X), Units.LengthNumber(point.Y), Units.LengthSymbol);
        }));
        buttons.Children.Add(ManageButton("SiteClose", GetText("Phases.Close"), (_, _) => window.Close()));

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Site.Coordinates.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var (label, box) in new (string, TextBox)[]
                 {
                     ("Site.Latitude", latitude), ("Site.Longitude", longitude), ("Site.Elevation", elevation),
                     ("Site.East", east), ("Site.North", north)
                 })
        {
            layout.Children.Add(new TextBlock { Text = GetText(label) });
            layout.Children.Add(box);
        }
        layout.Children.Add(new TextBlock { Text = GetText("Site.ProjectPoint") });
        layout.Children.Add(projectPoint);
        layout.Children.Add(new TextBlock { Text = GetText("Site.SharedPoint") });
        layout.Children.Add(sharedPoint);
        layout.Children.Add(result);
        layout.Children.Add(buttons);
        window.Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Show();
    }

    // ------------------------------------------------------------------ position (true north)

    private void ShowPositionWindow()
    {
        if (_positionWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }
        var window = NewManageWindow("Site.Position.Title", 420);
        window.Closed += (_, _) => _positionWindow = null;
        _positionWindow = window;
        var angle = SiteField("SiteTrueNorth", Plain(_session.Document.Site.TrueNorthDegrees));
        var sample = new TextBlock { Name = "SiteNorthSample", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

        void Describe()
        {
            if (!TryNumber(angle.Text, out var degrees)) { sample.Text = GetText("Status.SiteInvalid"); return; }
            var rotated = _session.Document.Site with { TrueNorthDegrees = degrees };
            var (x, y) = rotated.TrueToProject(0, 1);
            sample.Text = FormatText("Site.NorthSample", x, y);
        }
        angle.TextChanged += (_, _) => Describe();

        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("SitePositionApply", GetText("Site.Apply"), (_, _) =>
        {
            if (!TryNumber(angle.Text, out var degrees) || !(_session.Document.Site with { TrueNorthDegrees = degrees }).IsValid)
            {
                StatusText.Text = GetText("Status.SiteInvalid");
                return;
            }
            try
            {
                _session.SetProjectSite(_session.Document.Site with { TrueNorthDegrees = degrees });
                StatusText.Text = FormatText("Status.NorthChanged", degrees);
                RefreshAll();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("SitePositionClose", GetText("Phases.Close"), (_, _) => window.Close()));

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Site.Position.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Text = GetText("Site.TrueNorth") });
        layout.Children.Add(angle);
        layout.Children.Add(sample);
        layout.Children.Add(buttons);
        window.Content = layout;
        Describe();
        window.Show();
    }

    /// <summary>A small north arrow in the corner of the plan, pointing to true north as the project is turned.</summary>
    private void AddNorthArrow()
    {
        var north = _session.Document.Site.NorthDirection;
        var centre = new Point(PlanCanvas.ActualWidth - 38, 42);
        // Canvas y runs down, the plan's y runs up.
        var direction = new Vector(north.X, -north.Y);
        var tip = centre + direction * 22;
        var tail = centre - direction * 14;
        var side = new Vector(-direction.Y, direction.X);
        foreach (var (from, to, thickness) in new[]
                 {
                     (tail, tip, 2.0), (tip, tip - direction * 9 + side * 5, 2.0), (tip, tip - direction * 9 - side * 5, 2.0)
                 })
            PlanCanvas.Children.Add(new Line
            {
                X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = Brushes.DimGray, StrokeThickness = thickness, IsHitTestVisible = false,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
        var label = new TextBlock { Text = "N", FontWeight = FontWeights.Bold, Foreground = Brushes.DimGray, IsHitTestVisible = false };
        var beyond = centre + direction * 36;
        Canvas.SetLeft(label, beyond.X - 5);
        Canvas.SetTop(label, beyond.Y - 9);
        PlanCanvas.Children.Add(label);
    }
}
