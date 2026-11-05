using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Shows wall-specific editors only when their operations apply to the current selection.</summary>
    private void UpdateWallPropertyEditorVisibility()
    {
        var hasWall = _session.Document.Walls.Any(wall => wall.Id == _selectedWallId);
        var hasOpening = _session.Document.Openings.Any(opening => opening.Id == _selectedOpeningId);
        WallInstanceExpander.Visibility = hasWall ? Visibility.Visible : Visibility.Collapsed;
        WallTransformExpander.Visibility = hasWall ? Visibility.Visible : Visibility.Collapsed;
        WallOpeningExpander.Visibility = hasOpening ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Builds the scoped project-browser hierarchy and updates its visible result count.</summary>
    private void RefreshTree()
    {
        ProjectTree.Items.Clear();
        var project = new TreeViewItem { Header = _session.Document.Name, IsExpanded = true };
        var scope = GetProjectBrowserScope();
        var hasQuery = !string.IsNullOrWhiteSpace(ProjectSearchInput?.Text);
        if (scope is "All" or "Views") AppendViewBranches(project);
        if (scope is "All" or "Model") AppendModelBranches(project, hasQuery);
        AppendExtendedProjectTree(project);
        ProjectTree.Items.Add(project);
        var resultCount = CountProjectBrowserItems(project);
        ProjectBrowserCountText.Text = FormatText("Browser.ResultCount", resultCount);
        ProjectTree.Visibility = resultCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        ProjectBrowserEmptyState.Visibility = resultCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Adds coordinated views and callouts that match the current project-browser query.</summary>
    private void AppendViewBranches(TreeViewItem project)
    {
        var views = new TreeViewItem
        {
            Header = GetText("Tree.Views"), IsExpanded = true, ContextMenu = CreateAllViewsMenu()
        };
        foreach (var view in _session.Document.Views.Where(view => MatchesProjectSearch(view.Name)))
            views.Items.Add(new TreeViewItem
            {
                Header = view.Name, Tag = view.Id, ContextMenu = CreateViewMenu(view.Id)
            });
        project.Items.Add(views);
        if (CreateCalloutBranch() is { } calloutBranch) project.Items.Add(calloutBranch);
    }

    /// <summary>Adds levels, walls, and hosted openings while preserving useful hierarchy for search matches.</summary>
    private void AppendModelBranches(TreeViewItem project, bool hasQuery)
    {
        var levels = new TreeViewItem { Header = GetText("Tree.Levels"), IsExpanded = true };
        foreach (var level in _session.Document.Levels)
        {
            var levelLabel = $"{level.Name}  ({level.Elevation:0.##} m)";
            var levelMatches = MatchesProjectSearch(levelLabel);
            var levelBranch = new TreeViewItem
            {
                Header = levelLabel, Tag = level.Id, IsExpanded = !hasQuery || levelMatches
            };
            AppendWallBranches(levelBranch, level.Id, hasQuery, levelMatches);
            if (hasQuery && !levelMatches && levelBranch.Items.Count == 0) continue;
            levels.Items.Add(levelBranch);
        }
        project.Items.Add(levels);
    }

    /// <summary>Adds matching walls and openings for one level without flattening their host relationship.</summary>
    private void AppendWallBranches(TreeViewItem levelBranch, Guid levelId, bool hasQuery, bool levelMatches)
    {
        foreach (var wall in _session.Document.Walls.Where(candidate => candidate.LevelId == levelId))
        {
            var wallLabel = $"{GetText("Entity.Wall")} · {Units.FormatLength(wall.Length)}";
            var wallMatches = MatchesProjectSearch(wallLabel);
            var wallBranch = new TreeViewItem { Header = wallLabel, Tag = wall.Id, IsExpanded = hasQuery };
            foreach (var opening in _session.Document.Openings.Where(candidate => candidate.WallId == wall.Id))
            {
                var openingLabel = $"{GetText(opening.Kind == OpeningKind.Door ? "Entity.Door" : "Entity.Window")} · {Units.FormatLength(opening.Width)}";
                if (!levelMatches && !wallMatches && !MatchesProjectSearch(openingLabel)) continue;
                wallBranch.Items.Add(new TreeViewItem { Header = openingLabel, Tag = opening.Id });
            }
            if (hasQuery && !levelMatches && !wallMatches && wallBranch.Items.Count == 0) continue;
            levelBranch.Items.Add(wallBranch);
        }
    }

    /// <summary>Adds model and documentation branches allowed by the active project-browser scope.</summary>
    private void AppendExtendedProjectTree(TreeViewItem project)
    {
        var query = ProjectSearchInput?.Text?.Trim() ?? string.Empty;
        var scope = GetProjectBrowserScope();
        if (scope is "All" or "Model")
        {
            var types = new TreeViewItem { Header = GetText("Tree.WallTypes"), IsExpanded = query.Length > 0 };
            foreach (var type in _session.Document.WallTypes.Where(type => MatchesProjectSearch(type.Name)))
                types.Items.Add(new TreeViewItem { Header = type.Name, Tag = type.Id });
            var rooms = new TreeViewItem { Header = GetText("Tree.Rooms"), IsExpanded = query.Length > 0 };
            foreach (var room in _session.Document.Rooms.Where(room =>
                         MatchesProjectSearch(room.Name) || MatchesProjectSearch(room.Number)))
                rooms.Items.Add(new TreeViewItem { Header = $"{room.Number} · {room.Name}", Tag = room.Id });
            var floors = new TreeViewItem { Header = GetText("Tree.Floors"), IsExpanded = query.Length > 0 };
            foreach (var floor in _session.Document.Floors.Where(floor => MatchesProjectSearch(floor.Name)))
                floors.Items.Add(new TreeViewItem { Header = floor.Name, Tag = floor.Id });
            project.Items.Add(types);
            project.Items.Add(rooms);
            project.Items.Add(floors);
        }
        if (scope is not ("All" or "Documentation")) return;
        var sheets = new TreeViewItem { Header = GetText("Tree.Sheets"), IsExpanded = true };
        foreach (var sheet in _session.Document.Sheets.Where(sheet =>
                     MatchesProjectSearch(sheet.Name) || MatchesProjectSearch(sheet.Number)))
            sheets.Items.Add(new TreeViewItem { Header = $"{sheet.Number} · {sheet.Name}", Tag = sheet.Id });
        project.Items.Add(sheets);
    }

    /// <summary>Determines whether a project-browser label matches the active search query.</summary>
    private bool MatchesProjectSearch(string value)
    {
        var query = ProjectSearchInput?.Text?.Trim() ?? string.Empty;
        return query.Length == 0 || value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Clears the project-browser query immediately and restores keyboard focus to the search field.</summary>
    private void ClearProjectSearch_Click(object sender, RoutedEventArgs e)
    {
        ProjectSearchInput.Clear();
        ProjectSearchInput.Focus();
    }

    /// <summary>Rebuilds the project browser when its professional view, model, or documentation scope changes.</summary>
    private void ProjectBrowserScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectTree is not null && _session is not null) RefreshTree();
    }

    /// <summary>Returns the stable scope identifier attached to the project-browser scope selector.</summary>
    private string GetProjectBrowserScope() =>
        ProjectBrowserScopePicker?.SelectedItem is ComboBoxItem { Tag: string scope } ? scope : "All";

    /// <summary>Counts selectable BIM objects without including organizational branch headings.</summary>
    private static int CountProjectBrowserItems(TreeViewItem branch)
    {
        var count = branch.Tag is Guid ? 1 : 0;
        foreach (var child in branch.Items.OfType<TreeViewItem>()) count += CountProjectBrowserItems(child);
        return count;
    }
}
