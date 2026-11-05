using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _contextWindow;

    /// <summary>Routes the Mass and Site buttons: masses and what is built from them, parking spaces and the site's sun data.</summary>
    private bool TryExecuteMassCommand(string commandId)
    {
        switch (commandId)
        {
            case "PlaceMass": BeginKindTool(StructuralElementKind.Mass); return true;
            case "InPlaceMass": BeginInPlaceMass(); return true;
            case "ShowMass": ToggleMassVisibility(); return true;
            case "RoofByFace": BuildFromSelectedMass(roof: true); return true;
            // Walls by face is also the structural foundation tool: only a selected mass makes it build from the mass.
            case "WallByFace" when SelectedMass() is not null: BuildFromSelectedMass(roof: false); return true;
            case "ParkingComponent": BeginKindTool(StructuralElementKind.ParkingSpace); return true;
            case "ContextualData": ShowSiteContextWindow(); return true;
            default: return false;
        }
    }

    private StructuralElement? SelectedMass() =>
        _selectedStructuralId is { } id ? _session.Document.StructuralElements.FirstOrDefault(element => element.Id == id && element.Kind == StructuralElementKind.Mass) : null;

    /// <summary>Asks how tall the mass is and arms the tool; the next two clicks draw its outline.</summary>
    private void BeginInPlaceMass()
    {
        var text = TextPrompt.Ask(this, GetText("Mass.InPlace"), GetText("Prompt.Mass.Height"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"),
            Units.LengthNumber(9));
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!Units.TryParseLength(text, out var height) || height < 0.2 || height > 500)
        {
            StatusText.Text = GetText("Status.MassHeightInvalid");
            return;
        }
        BeginPresetTool(new ElementPreset(GetText("Entity.Mass"), StructuralElementKind.Mass, ElementStyle.Default, 0, height, 0));
    }

    /// <summary>Hides or shows the masses in the active view; the masses themselves do not change.</summary>
    private void ToggleMassVisibility()
    {
        if (ActiveProjectView() is not { } view)
        {
            StatusText.Text = GetText("Status.NoViewForCategories");
            return;
        }
        var hidden = view.HiddenCategories.ToList();
        var nowHidden = !hidden.Remove(ViewCategory.Masses);
        if (nowHidden) hidden.Add(ViewCategory.Masses);
        _session.SetViewHiddenCategories(view.Id, hidden);
        ApplyViewVisibility();
        StatusText.Text = GetText(nowHidden ? "Status.MassesHidden" : "Status.MassesShown");
    }

    /// <summary>Builds the walls (or the roof) of the selected mass, as one undoable step.</summary>
    private void BuildFromSelectedMass(bool roof)
    {
        if (SelectedMass() is not { } mass)
        {
            StatusText.Text = GetText("Status.MassNotSelected");
            return;
        }
        try
        {
            if (roof)
            {
                _session.AddRoofOnMass(mass.Id);
                StatusText.Text = GetText("Status.MassRoofBuilt");
            }
            else
            {
                var walls = _session.AddWallsOnMass(mass.Id, SelectedWallTypeIdOrDefault());
                StatusText.Text = FormatText("Status.MassWallsBuilt", walls.Count);
            }
            RefreshAll();
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    private Guid SelectedWallTypeIdOrDefault() =>
        WallTypePicker?.SelectedItem is ComboBoxItem { Tag: Guid id } && _session.Document.WallTypes.Any(type => type.Id == id) ? id : BuiltInWallTypes.Basic200Id;

    /// <summary>Opens the site's sun data for the four key days of the year at the project's latitude.</summary>
    private void ShowSiteContextWindow()
    {
        if (_contextWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }
        var window = NewManageWindow("Context.Title", 480);
        window.Closed += (_, _) => _contextWindow = null;
        _contextWindow = window;
        var site = _session.Document.Site;
        var text = new TextBlock { Name = "ContextText", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        text.Text = SiteContextText(site);
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Context.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(text);
        layout.Children.Add(ManageButton("ContextPosition", GetText("Context.Edit"), (_, _) => ShowCoordinatesWindow()));
        layout.Children.Add(ManageButton("ContextClose", GetText("Phases.Close"), (_, _) => window.Close()));
        window.Content = layout;
        window.Show();
    }

    /// <summary>The place and the sun's day length, sunrise, sunset and noon height on the four key days.</summary>
    internal string SiteContextText(ProjectSite site)
    {
        string Hour(double? value) => value is { } hours ? $"{(int)hours:00}:{(int)((hours - (int)hours) * 60):00}" : "—";
        var lines = new List<string>
        {
            FormatText("Context.Place", site.Latitude, site.Longitude, site.ElevationMeters, site.TrueNorthDegrees)
        };
        foreach (var day in SiteSolarData.ForLatitude(site.Latitude))
            lines.Add(FormatText("Context.Day", GetText("Context.Day." + day.Name), Hour(day.SunriseHour), Hour(day.SunsetHour), day.DaylightHours, day.NoonAltitudeDegrees));
        return string.Join(Environment.NewLine, lines);
    }
}
