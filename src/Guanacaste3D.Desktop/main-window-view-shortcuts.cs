using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>Coordinates the compact top bar for elevation views and the simple plan-view range editor.</summary>
public partial class MainWindow
{
    /// <summary>Rebuilds one consistently styled shortcut for every elevation in the project.</summary>
    private void RefreshViewShortcuts()
    {
        if (ViewShortcutButtons is null || _session is null) return;
        ViewShortcutButtons.Children.Clear();
        foreach (var view in _session.Document.Views.Where(candidate => candidate.Kind == ProjectViewKind.Elevation))
            ViewShortcutButtons.Children.Add(CreateElevationShortcut(view));
    }

    /// <summary>Creates an accessible elevation chip with open and delete actions.</summary>
    private FrameworkElement CreateElevationShortcut(ProjectView view)
    {
        var active = view.Id == _elevationViewId;
        var container = new Border
        {
            Background = active ? new SolidColorBrush(Color.FromRgb(229, 240, 252)) : Brushes.White,
            BorderBrush = active ? new SolidColorBrush(Color.FromRgb(0, 112, 192)) : new SolidColorBrush(Color.FromRgb(205, 209, 214)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0)
        };
        var panel = new DockPanel();
        var remove = new Button
        {
            Content = new TextBlock { Text = "\uE711", FontFamily = (FontFamily)FindResource("GlyphFont"), FontSize = 9 },
            Width = 24, Padding = new Thickness(2), ToolTip = GetText("Elevation.Delete"),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0)
        };
        remove.Click += (_, _) => RemoveElevationView(view.Id);
        DockPanel.SetDock(remove, Dock.Right);
        var open = new Button
        {
            Content = CreateShortcutContent(view, active),
            Padding = new Thickness(10, 4, 7, 4), FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = GetText("Elevation.CoverageHint")
        };
        open.Click += (_, _) => SelectProjectView(view.Id);
        panel.Children.Add(remove);
        panel.Children.Add(open);
        container.Child = panel;
        return container;
    }

    /// <summary>Builds a compact cardinal badge and readable view name for an elevation shortcut.</summary>
    private FrameworkElement CreateShortcutContent(ProjectView view, bool active)
    {
        var direction = OrientationLabel(view.Orientation);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Border
        {
            Background = active ? new SolidColorBrush(Color.FromRgb(0, 112, 192)) : new SolidColorBrush(Color.FromRgb(231, 234, 238)),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(0, 0, 7, 0),
            Child = new TextBlock
            {
                Text = direction[..1].ToUpper(System.Globalization.CultureInfo.CurrentCulture),
                Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 65, 81)),
                FontWeight = FontWeights.Bold, ToolTip = direction
            }
        });
        content.Children.Add(new TextBlock { Text = view.Name, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    /// <summary>Returns a short localized cardinal label for an elevation chip.</summary>
    private string OrientationLabel(ViewOrientation orientation) => GetText("Elevation." + orientation);

    /// <summary>Opens the creation dialog and selects the resulting elevation.</summary>
    private void AddElevationView_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ElevationViewWindow(_session.Document.Views.Count(view => view.Kind == ProjectViewKind.Elevation) + 1)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var id = _session.AddElevationView(dialog.ViewName, dialog.Orientation);
            _elevationViewId = id;
            StatusText.Text = GetText("Status.ElevationAdded");
            RefreshAll();
            SelectProjectView(id);
        }
        catch (InvalidDataException exception)
        {
            ShowViewError(exception, GetText("Elevation.NewTitle"));
        }
    }

    /// <summary>Confirms and deletes an elevation, preserving a valid active elevation afterward.</summary>
    private void RemoveElevationView(Guid id)
    {
        var view = _session.Document.Views.FirstOrDefault(candidate => candidate.Id == id);
        if (view is null) return;
        if (MessageBox.Show(this, FormatText("Elevation.DeleteConfirm", view.Name), GetText("Elevation.DeleteTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        try
        {
            _session.RemoveElevationView(id);
            if (_elevationViewId == id)
                _elevationViewId = _session.Document.Views.First(candidate => candidate.Kind == ProjectViewKind.Elevation).Id;
            _selectedViewId = null;
            StatusText.Text = GetText("Status.ElevationRemoved");
            RefreshAll();
        }
        catch (InvalidDataException exception)
        {
            ShowViewError(exception, GetText("Elevation.DeleteTitle"));
        }
    }

    /// <summary>Opens the range editor for the active level's plan and commits the selected offsets.</summary>
    private void EditPlanRange_Click(object sender, RoutedEventArgs e)
    {
        var level = _session.Document.Levels.First(candidate => candidate.Id == _activeLevelId);
        var plan = _session.Document.Views.First(candidate => candidate.Kind == ProjectViewKind.Plan && candidate.LevelId == level.Id);
        var above = _session.Document.Levels.Where(candidate => candidate.Elevation > level.Elevation)
            .OrderBy(candidate => candidate.Elevation).FirstOrDefault();
        var height = above?.Elevation - level.Elevation ?? Math.Max(3, plan.Range?.TopOffset ?? 3);
        var dialog = new PlanViewRangeWindow(level.Name, plan.Range ?? PlanViewRange.Architectural, height) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _session.UpdatePlanViewRange(plan.Id, dialog.Range);
            StatusText.Text = GetText("Status.ViewRangeUpdated");
            RefreshAll();
        }
        catch (InvalidDataException exception)
        {
            ShowViewError(exception, GetText("ViewRange.Title"));
        }
    }

    /// <summary>Shows a localized validation error for a view operation.</summary>
    private void ShowViewError(InvalidDataException exception, string title) => MessageBox.Show(
        this, LocalizedErrors.Translate(exception.Message), title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
