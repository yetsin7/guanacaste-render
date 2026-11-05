using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Marks a detail region around the selected element and names it.</summary>
    private void CreateCalloutFromSelection()
    {
        var selected = _selectedOpeningId ?? _selectedColumnId ?? _selectedStructuralId ??
            _selectedRoomId ?? _selectedFloorId ?? _selectedWallId;
        if (selected is not Guid id)
        {
            StatusText.Text = GetText("Callout.SelectFirst");
            return;
        }
        var next = _session.Document.Callouts.Count(callout => callout.LevelId == _activeLevelId) + 1;
        var name = PromptWindow.Ask(this, GetText("Callout.NameTitle"), FormatText("Callout.DefaultName", next));
        if (name is null) return;
        try
        {
            var calloutId = _session.AddCalloutAround(id, 1.0, name);
            RefreshAll();
            ZoomToCallout(calloutId);
        }
        catch (InvalidDataException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>Opens the plan of the callout's level and zooms to its region.</summary>
    private void ZoomToCallout(Guid calloutId)
    {
        if (_session.Document.Callouts.FirstOrDefault(item => item.Id == calloutId) is not { } callout) return;
        _activeLevelId = callout.LevelId;
        ViewTabs.SelectedIndex = 0;
        var region = callout.Region;
        var width = Math.Max(1, PlanCanvas.ActualWidth - 100);
        var height = Math.Max(1, PlanCanvas.ActualHeight - 100);
        _scale = Math.Clamp(Math.Min(width / (region.MaxX - region.MinX), height / (region.MaxY - region.MinY)),
            MinimumPlanScale, MaximumPlanScale);
        _panOffset = new Point(-(region.MinX + region.MaxX) / 2 * _scale, (region.MinY + region.MaxY) / 2 * _scale);
        RefreshAll();
        StatusText.Text = FormatText("Callout.Opened", callout.Name);
    }

    /// <summary>Draws each callout of the active level as a dashed frame with a name tab that zooms to it.</summary>
    private void RenderCallouts()
    {
        foreach (var callout in _session.Document.Callouts.Where(item => item.LevelId == _activeLevelId))
        {
            var corner = WorldToCanvas(new Point2(callout.Region.MinX, callout.Region.MaxY));
            var opposite = WorldToCanvas(new Point2(callout.Region.MaxX, callout.Region.MinY));
            var frame = new Rectangle
            {
                Width = Math.Abs(opposite.X - corner.X), Height = Math.Abs(opposite.Y - corner.Y),
                Stroke = Brushes.DarkOrange, StrokeThickness = 1.5, StrokeDashArray = [6, 3], IsHitTestVisible = false
            };
            Canvas.SetLeft(frame, Math.Min(corner.X, opposite.X));
            Canvas.SetTop(frame, Math.Min(corner.Y, opposite.Y));
            PlanCanvas.Children.Add(frame);
            var id = callout.Id;
            var tab = new Button
            {
                Content = callout.Name, FontSize = 10, Foreground = Brushes.White, Background = Brushes.DarkOrange,
                BorderThickness = new Thickness(0), Padding = new Thickness(5, 1, 5, 1), MinHeight = 0, MinWidth = 0,
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = GetText("Callout.Zoom")
            };
            tab.Click += (_, _) => ZoomToCallout(id);
            Canvas.SetLeft(tab, Math.Min(corner.X, opposite.X));
            Canvas.SetTop(tab, Math.Min(corner.Y, opposite.Y) - 18);
            PlanCanvas.Children.Add(tab);
        }
    }

    /// <summary>Adds the project browser's callout branch; each entry zooms to its region, and its menu deletes it.</summary>
    private TreeViewItem? CreateCalloutBranch()
    {
        if (_session.Document.Callouts.Count == 0) return null;
        var branch = new TreeViewItem { Header = GetText("Tree.Callouts"), IsExpanded = true };
        foreach (var callout in _session.Document.Callouts.Where(item => MatchesProjectSearch(item.Name)))
        {
            var id = callout.Id;
            var remove = new MenuItem { Header = GetText("Callout.Delete") };
            remove.Click += (_, _) =>
            {
                _session.RemoveCallout(id);
                RefreshAll();
            };
            branch.Items.Add(new TreeViewItem { Header = callout.Name, Tag = id, ContextMenu = new ContextMenu { Items = { remove } } });
        }
        return branch;
    }

    /// <summary>Opens a menu to save the active view as a template or apply, or delete, a saved one.</summary>
    private void ShowViewTemplatesMenu()
    {
        var menu = new ContextMenu { PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        var save = new MenuItem { Header = GetText("ViewTemplate.SaveMenu") };
        save.Click += (_, _) => SaveViewTemplateFromView();
        menu.Items.Add(save);

        var apply = new MenuItem { Header = GetText("ViewTemplate.Apply") };
        if (ActiveProjectView() is { } active) AddTemplateItems(apply, [active.Id]);
        else apply.IsEnabled = false;
        menu.Items.Add(apply);

        var delete = new MenuItem { Header = GetText("ViewTemplate.Delete") };
        if (_session.Document.ViewTemplates.Count == 0) delete.IsEnabled = false;
        foreach (var template in _session.Document.ViewTemplates)
        {
            var id = template.Id;
            var item = new MenuItem { Header = template.Name };
            item.Click += (_, _) =>
            {
                _session.RemoveViewTemplate(id);
                StatusText.Text = GetText("ViewTemplate.Deleted");
            };
            delete.Items.Add(item);
        }
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }
}
