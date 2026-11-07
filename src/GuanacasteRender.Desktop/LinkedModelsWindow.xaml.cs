using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core.Import;

namespace Guanacaste3D.Desktop;

/// <summary>Lists the models linked into the project and edits their visibility and placement.</summary>
public partial class LinkedModelsWindow : Window
{
    private readonly LinkedModels _links;
    private readonly Func<Task> _add;
    private bool _refreshing;

    /// <summary>One row of the list; the check box writes straight back to <see cref="IsVisible"/>.</summary>
    internal sealed class Row
    {
        public required ModelLink Link { get; init; }
        public bool IsVisible { get; set; }
        public string Name => Link.Name;
        public required string Triangles { get; init; }
        public required string State { get; init; }
    }

    internal LinkedModelsWindow(LinkedModels links, Func<Task> add)
    {
        InitializeComponent();
        _links = links;
        _add = add;
        Refresh();
    }

    private Row? Selected => LinkList.SelectedItem as Row;

    private void Refresh()
    {
        _refreshing = true;
        var selectedId = Selected?.Link.Id;
        LinkList.ItemsSource = _links.Links.Select(link => new Row
        {
            Link = link,
            IsVisible = link.Visible,
            Triangles = _links.Get(link.Id) is { } loaded ? loaded.Model.TriangleCount.ToString("N0", CultureInfo.CurrentCulture) : "—",
            State = _links.IsLoaded(link.Id) ? UiText.Get("Links.Loaded")
                : _links.ErrorOf(link.Id) is { } error ? LocalizedErrors.Translate(error) : UiText.Get("Links.Missing")
        }).ToList();
        EmptyText.Visibility = _links.Links.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (selectedId is { } id) LinkList.SelectedItem = LinkList.Items.OfType<Row>().FirstOrDefault(row => row.Link.Id == id);
        _refreshing = false;
        LoadFields();
    }

    private void LinkList_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadFields();

    private void LoadFields()
    {
        DetailPanel.IsEnabled = Selected is not null;
        if (Selected is not { } row) return;
        var p = row.Link.Placement;
        PathText.Text = row.Link.Path;
        OffsetXInput.Text = Format(p.OffsetX);
        OffsetYInput.Text = Format(p.OffsetY);
        OffsetZInput.Text = Format(p.OffsetZ);
        RotationInput.Text = Format(p.RotationDegrees);
        ScaleInput.Text = Format(p.Scale);
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void Visible_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing || (sender as FrameworkElement)?.DataContext is not Row row) return;
        _links.Update(row.Link.Id, link => link.Visible = row.IsVisible);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (!TryParse(OffsetXInput.Text, out var x) || !TryParse(OffsetYInput.Text, out var y) || !TryParse(OffsetZInput.Text, out var z) ||
            !TryParse(RotationInput.Text, out var rotation) || !TryParse(ScaleInput.Text, out var scale) ||
            !new ModelPlacement(x, y, z, rotation, scale).IsValid)
        {
            MessageBox.Show(this, UiText.Get("Links.InvalidPlacement"), UiText.Get("Links.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _links.Update(row.Link.Id, link => link.Placement = new ModelPlacement(x, y, z, rotation, scale));
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        await _links.ReloadAsync(row.Link.Id, CancellationToken.None);
        Refresh();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        _links.Remove(row.Link.Id);
        Refresh();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        await _add();
        Refresh();
    }
}
