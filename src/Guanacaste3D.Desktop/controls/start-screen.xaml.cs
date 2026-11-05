using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>What the Home screen asks the application to do.</summary>
public interface IStartScreenHost
{
    RecentDocuments Recents { get; }
    IReadOnlyList<StartSample> Samples { get; }
    bool HasOpenProject { get; }
    void NewProject();
    void OpenProjectDialog();
    void OpenProject(string path);
    void OpenFamilyDialog();
    void NewFamily();
    void OpenSample(StartSample sample);
    void ShowAbout();
    void ReturnToProject();
}

/// <summary>
/// A sample project offered on the Home screen. <see cref="Thumbnail"/> is a picture shipped with the application;
/// without one, a line drawing is made from <see cref="Build"/> the first time and cached.
/// </summary>
public sealed record StartSample(string Id, string TitleKey, string DetailKey, Func<BuildingDocument> Build, string? Thumbnail = null);

/// <summary>One tile or row of the Home screen.</summary>
public sealed class StartItem : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;

    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string Badge { get; init; }
    public string Location { get; init; } = string.Empty;
    /// <summary>The name of the folder holding the file, shown under the date so projects with the same name can be told apart.</summary>
    public string Folder { get; init; } = string.Empty;
    /// <summary>True when the file is no longer where it was; the tile is dimmed and says so.</summary>
    public bool Missing { get; init; }
    public double TileOpacity => Missing ? 0.55 : 1;
    public string ToolTip { get; init; } = string.Empty;
    public string? Path { get; init; }
    public StartSample? Sample { get; init; }
    public DateTime Modified { get; init; }
    public DateTime FileModified { get; init; }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The Home screen shown when the application starts and from File &gt; Home: open or start a project or a family, or
/// pick one of the recent projects or samples, shown as tiles or a list, sorted by date or name and filtered by a search.
/// </summary>
public partial class StartScreen : UserControl
{
    private IStartScreenHost? _host;
    private string _fileFilter = "All";
    private readonly Dictionary<string, ImageSource> _thumbnails = new(StringComparer.OrdinalIgnoreCase);

    public StartScreen()
    {
        InitializeComponent();
    }

    public void Attach(IStartScreenHost host)
    {
        _host = host;
        Refresh();
    }

    /// <summary>Rebuilds the visible page, for example after a project was opened or saved.</summary>
    public void Refresh()
    {
        if (_host is null) return;
        BackToProjectLink.Visibility = _host.HasOpenProject ? Visibility.Visible : Visibility.Collapsed;
        var samples = SamplesTab.IsChecked == true;
        PageTitle.Text = Text(samples ? "Start.Samples" : "Start.Recent");
        SearchHint.Text = Text(samples ? "Start.SearchSamples" : "Start.Search");
        IEnumerable<StartItem> items = samples ? SampleItems() : RecentItems();
        if (!samples && _fileFilter != "All")
            items = items.Where(item => string.Equals(item.Badge, _fileFilter, StringComparison.OrdinalIgnoreCase));
        var query = SearchBox.Text.Trim();
        if (query.Length > 0)
            items = items.Where(item => item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                        item.Location.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        var sortMode = (SortPicker.SelectedItem as ComboBoxItem)?.Tag as string;
        var descending = DescendingButton.IsChecked == true;
        items = sortMode switch
        {
            "Name" => descending
                ? items.OrderByDescending(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
            "Modified" => descending
                ? items.OrderByDescending(item => item.FileModified)
                : items.OrderBy(item => item.FileModified),
            _ => descending
                ? items.OrderByDescending(item => item.Modified)
                : items.OrderBy(item => item.Modified)
        };
        var list = items.ToList();
        Items.ItemsSource = list;
        EmptyText.Text = Text(query.Length > 0 ? "Start.NoMatches" : "Start.Empty");
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private IEnumerable<StartItem> RecentItems()
    {
        if (!DesktopOptions.Load().ShowRecentFiles) yield break;
        foreach (var recent in _host!.Recents.Load())
        {
            // Recents are intentionally ordered by the last time the user opened the project, not by when Windows last
            // modified the file. This keeps the projects the person actually worked on at the top of Home.
            var lastOpened = recent.LastOpened;
            var exists = File.Exists(recent.Path);
            var directory = System.IO.Path.GetDirectoryName(recent.Path) ?? string.Empty;
            var item = new StartItem
            {
                Title = System.IO.Path.GetFileNameWithoutExtension(recent.Path), Path = recent.Path,
                Badge = System.IO.Path.GetExtension(recent.Path).TrimStart('.').ToUpperInvariant(),
                Detail = exists ? lastOpened.ToString("d/M/yy, H:mm", CultureInfo.CurrentCulture) : Text("Start.Missing"),
                Location = directory, Missing = !exists,
                Folder = System.IO.Path.GetFileName(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar)),
                ToolTip = recent.Path,
                Modified = lastOpened,
                FileModified = File.GetLastWriteTime(recent.Path)
            };
            item.Thumbnail = CachedThumbnail(_host.Recents.ThumbnailPath(recent.Path)) ?? Placeholder();
            if (exists && !File.Exists(_host.Recents.ThumbnailPath(recent.Path)))
                _ = MakeThumbnailAsync(item, () => ProjectFile.Load(recent.Path), _host.Recents.ThumbnailPath(recent.Path));
            yield return item;
        }
    }

    private IEnumerable<StartItem> SampleItems()
    {
        foreach (var (sample, order) in _host!.Samples.Select((sample, index) => (sample, index)))
        {
            var item = new StartItem
            {
                Title = Text(sample.TitleKey), Detail = Text(sample.DetailKey), Badge = Text("Start.SampleBadge"),
                Sample = sample, ToolTip = Text(sample.DetailKey),
                // Samples keep the order the application lists them in when sorted "by date".
                Modified = DateTime.MaxValue.AddDays(-order),
                FileModified = DateTime.MaxValue.AddDays(-order)
            };
            var cached = System.IO.Path.Combine(_host.Recents.ThumbnailFolder, $"sample-v4-{sample.Id}.png");
            item.Thumbnail = CachedThumbnail(sample.Thumbnail ?? cached) ?? Placeholder();
            if (sample.Thumbnail is null && !File.Exists(cached)) _ = MakeThumbnailAsync(item, sample.Build, cached);
            yield return item;
        }
    }

    private ImageSource? CachedThumbnail(string path)
    {
        if (_thumbnails.TryGetValue(path, out var image)) return image;
        if (ProjectThumbnail.Load(path) is not { } loaded) return null;
        return _thumbnails[path] = loaded;
    }

    /// <summary>
    /// Empty projects still get a meaningful architectural preview instead of a blank tile. The crosshair/grid also
    /// communicates the model origin until real geometry is available for a generated thumbnail.
    /// </summary>
    private static ImageSource Placeholder()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 128, 128))));

        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(232, 236, 240)), 1);
        for (var p = 16d; p < 128; p += 16)
        {
            group.Children.Add(new GeometryDrawing(null, gridPen, new LineGeometry(new Point(p, 0), new Point(p, 128))));
            group.Children.Add(new GeometryDrawing(null, gridPen, new LineGeometry(new Point(0, p), new Point(128, p))));
        }

        var xPen = new Pen(new SolidColorBrush(Color.FromRgb(72, 125, 190)), 1.6);
        var yPen = new Pen(new SolidColorBrush(Color.FromRgb(86, 155, 92)), 1.6);
        group.Children.Add(new GeometryDrawing(null, xPen, new LineGeometry(new Point(16, 64), new Point(112, 64))));
        group.Children.Add(new GeometryDrawing(null, yPen, new LineGeometry(new Point(64, 112), new Point(64, 16))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(52, 52, 52)), null,
            new EllipseGeometry(new Point(64, 64), 2.5, 2.5)));

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>Draws a missing thumbnail: the model is read and projected off the UI thread, then drawn and cached.</summary>
    private async Task MakeThumbnailAsync(StartItem item, Func<BuildingDocument> load, string cache)
    {
        try
        {
            var lines = await Task.Run(() => ProjectThumbnail.Faces(load()));
            // An empty project (a template) keeps the placeholder rather than a blank picture.
            if (lines.Count == 0) return;
            var image = ProjectThumbnail.Render(lines);
            ProjectThumbnail.Save(image, cache);
            _thumbnails[cache] = image;
            item.Thumbnail = image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            // An unreadable project keeps the placeholder; opening it reports the problem.
        }
    }

    private void Open(StartItem? item)
    {
        if (item is null || _host is null) return;
        if (item.Sample is { } sample) _host.OpenSample(sample);
        else if (item.Path is { } path)
        {
            if (File.Exists(path)) _host.OpenProject(path);
            else if (MessageBox.Show(Window.GetWindow(this), Text("Start.MissingPrompt"), Text("App.Name"),
                         MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _host.Recents.Remove(path);
                Refresh();
            }
        }
    }

    private static string Text(string key) => UiText.Get(key);

    private void OpenProject_Click(object sender, RoutedEventArgs e) => _host?.OpenProjectDialog();
    private void NewProject_Click(object sender, RoutedEventArgs e) => _host?.NewProject();
    private void OpenFamily_Click(object sender, RoutedEventArgs e) => _host?.OpenFamilyDialog();
    private void NewFamily_Click(object sender, RoutedEventArgs e) => _host?.NewFamily();
    private void About_Click(object sender, RoutedEventArgs e) => _host?.ShowAbout();
    private void BackToProject_Click(object sender, RoutedEventArgs e) => _host?.ReturnToProject();
    private void Page_Checked(object sender, RoutedEventArgs e) => Refresh();
    private void Sort_Changed(object sender, SelectionChangedEventArgs e) => Refresh();

    private void Direction_Click(object sender, RoutedEventArgs e)
    {
        DescendingButton.Content = DescendingButton.IsChecked == true ? "" : "";
        Refresh();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        FilterMenu.PlacementTarget = FilterButton;
        FilterMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        FilterMenu.IsOpen = true;
    }

    private void FilterOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string filter }) return;
        _fileFilter = filter;
        foreach (var menuItem in FilterMenu.Items.OfType<MenuItem>())
            menuItem.IsChecked = string.Equals(menuItem.Tag as string, filter, StringComparison.OrdinalIgnoreCase);
        Refresh();
    }

    private void ListMode_Click(object sender, RoutedEventArgs e) => SetListMode(true);
    private void GridMode_Click(object sender, RoutedEventArgs e) => SetListMode(false);

    /// <summary>Switches between large tiles and a compact list with each file's folder.</summary>
    private void SetListMode(bool list)
    {
        ListModeButton.IsChecked = list;
        GridModeButton.IsChecked = !list;
        Items.ItemTemplate = (DataTemplate)FindResource(list ? "RowTemplate" : "TileTemplate");
        Items.ItemsPanel = list
            ? new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)))
            : new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
    }

    /// <summary>A single click on a tile opens it, as on a start page.</summary>
    private void Items_Click(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Items, (DependencyObject)e.OriginalSource) is ListBoxItem container)
            Open(container.DataContext as StartItem);
    }

    private void Items_DoubleClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Items_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Open(Items.SelectedItem as StartItem);
        e.Handled = true;
    }

    /// <summary>Only recent files can be shown in their folder or forgotten.</summary>
    private void ItemMenu_Opened(object sender, RoutedEventArgs e)
    {
        var recent = (Items.SelectedItem as StartItem)?.Path is not null;
        ShowInFolderItem.IsEnabled = recent;
        RemoveItem.IsEnabled = recent;
        OpenItem.IsEnabled = Items.SelectedItem is not null;
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e) => Open(Items.SelectedItem as StartItem);

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((Items.SelectedItem as StartItem)?.Path is { } path && File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if ((Items.SelectedItem as StartItem)?.Path is not { } path || _host is null) return;
        _host.Recents.Remove(path);
        Refresh();
    }
}
