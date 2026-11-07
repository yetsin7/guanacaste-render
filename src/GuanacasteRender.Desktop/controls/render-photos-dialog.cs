using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>What the user asked the batch to produce.</summary>
internal sealed record PhotoRenderOptions(IReadOnlyList<Guid> PhotoIds, string Size, string Quality, bool Jpeg,
    string Folder, bool OpenFolder);

/// <summary>
/// Lumion's "Render" window: pick one photo, several or all of them, the resolution and quality, and where the pictures go.
/// Everything is rendered one after another, each with its own camera, style and filters.
/// </summary>
internal sealed class RenderPhotosDialog : Window
{
    private static readonly (string LabelKey, string Tag)[] Sizes =
    [
        ("RenderPanel.SizeHd", "1280x720"), ("RenderPanel.SizeFullHd", "1920x1080"),
        ("RenderPanel.SizeQhd", "2560x1440"), ("RenderPanel.SizeUhd", "3840x2160")
    ];

    private static readonly (string LabelKey, string Tag)[] Qualities =
    [
        ("RenderPanel.Auto", "Auto"), ("RenderPanel.Draft", "Draft"), ("RenderPanel.Standard", "Standard"),
        ("RenderPanel.High", "High"), ("RenderPanel.Final", "Final")
    ];

    private readonly List<(CheckBox Box, Border Card, Guid Id)> _entries = [];
    private readonly ComboBox _size = new() { MinHeight = 30 };
    private readonly ComboBox _quality = new() { MinHeight = 30 };
    private readonly ComboBox _format = new() { MinHeight = 30 };
    private readonly TextBox _folder = new() { MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _open = new() { IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 6, 0, 0), Opacity = 0.75, FontSize = 12 };
    private readonly Button _accept = new() { MinWidth = 130, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0), IsDefault = true,
        FontWeight = FontWeights.SemiBold };

    public PhotoRenderOptions? Result { get; private set; }

    /// <summary>Builds the batch-photo workspace around the project's saved render scenes.</summary>
    public RenderPhotosDialog(IReadOnlyList<RenderSceneBookmark> photos, Guid? only, string folder)
    {
        Title = Text("RenderPhotos.Title");
        Width = 840;
        Height = 700;
        MinWidth = 680;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Background = Brush(32, 32, 34);
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock { Text = Text("RenderPhotos.Title"), FontSize = 21, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = photos.Count == 0
                ? Text("RenderPhotos.Empty")
                : Text("RenderPhotos.Instructions"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0), Foreground = Brush(174, 174, 180)
        });
        root.Children.Add(header);

        var options = BuildOptions(folder);
        DockPanel.SetDock(options, Dock.Bottom);
        root.Children.Add(options);

        var photoSection = new DockPanel { LastChildFill = true };
        if (photos.Count > 0)
        {
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
            DockPanel.SetDock(bar, Dock.Top);
            var selectionActions = new StackPanel { Orientation = Orientation.Horizontal };
            selectionActions.Children.Add(LinkButton(Text("RenderPhotos.SelectAll"), () => SetAll(true)));
            selectionActions.Children.Add(LinkButton(Text("RenderPhotos.SelectNone"), () => SetAll(false)));
            bar.Children.Add(selectionActions);
            bar.Children.Add(_summary);
            photoSection.Children.Add(bar);
            var photoGrid = new WrapPanel { Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var photo in photos)
            {
                var card = BuildPhotoCard(photo);
                var box = new CheckBox
                {
                    IsChecked = only is null || only == photo.Id,
                    Content = card,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(5),
                    Foreground = Brushes.White
                };
                box.Checked += (_, _) => { UpdatePhotoCard(box, card); UpdateSummary(); };
                box.Unchecked += (_, _) => { UpdatePhotoCard(box, card); UpdateSummary(); };
                photoGrid.Children.Add(box);
                _entries.Add((box, card, photo.Id));
                UpdatePhotoCard(box, card);
            }
            photoSection.Children.Add(new Border
            {
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Padding = new Thickness(4),
                Background = Brush(25, 25, 27), BorderBrush = Brush(61, 61, 66),
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = photoGrid }
            });
        }
        else photoSection.Children.Add(BuildEmptyState());
        root.Children.Add(photoSection);
        Content = root;
        UpdateSummary();
    }

    /// <summary>Builds the output settings and confirmation actions shown below the photo grid.</summary>
    private FrameworkElement BuildOptions(string folder)
    {
        var section = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var selectors = new UniformGrid { Columns = 3 };
        foreach (var (labelKey, tag) in Sizes) _size.Items.Add(new ComboBoxItem { Content = Text(labelKey), Tag = tag });
        _size.SelectedIndex = 1;
        foreach (var (labelKey, tag) in Qualities) _quality.Items.Add(new ComboBoxItem { Content = Text(labelKey), Tag = tag });
        _quality.SelectedIndex = 0;
        _format.Items.Add(new ComboBoxItem { Content = Text("RenderPhotos.FormatPng"), Tag = "png" });
        _format.Items.Add(new ComboBoxItem { Content = Text("RenderPhotos.FormatJpg"), Tag = "jpg" });
        _format.SelectedIndex = 0;
        selectors.Children.Add(SelectorCard(Text("RenderPhotos.Resolution"), _size));
        selectors.Children.Add(SelectorCard(Text("RenderPhotos.Quality"), _quality));
        selectors.Children.Add(SelectorCard(Text("RenderPhotos.Format"), _format));
        section.Children.Add(selectors);

        section.Children.Add(Label(Text("RenderPhotos.Destination")));
        _folder.Text = folder;
        var folderRow = new DockPanel();
        var browse = SecondaryButton(Text("RenderPhotos.Browse"));
        browse.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) => BrowseFolder();
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_folder);
        section.Children.Add(folderRow);
        _open.Content = Text("RenderPhotos.OpenFolder");
        section.Children.Add(_open);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        _accept.Background = Brush(22, 119, 200);
        _accept.Foreground = Brushes.White;
        _accept.BorderBrush = Brush(83, 166, 235);
        _accept.Click += (_, _) => Accept();
        buttons.Children.Add(_accept);
        var cancel = SecondaryButton(Text("RenderPhotos.Cancel"));
        cancel.MinWidth = 90;
        cancel.IsCancel = true;
        buttons.Children.Add(cancel);
        section.Children.Add(buttons);
        return section;
    }

    /// <summary>Creates a thumbnail card for one saved render scene.</summary>
    private static Border BuildPhotoCard(RenderSceneBookmark photo)
    {
        var content = new StackPanel();
        content.Children.Add(new Image
        {
            Width = 150, Height = 84, Stretch = Stretch.UniformToFill,
            Source = PhotoThumbnails.Of(photo)
        });
        content.Children.Add(new TextBlock
        {
            Text = photo.Name, Margin = new Thickness(8, 7, 8, 8), Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150
        });
        return new Border
        {
            Width = 166, Background = Brush(42, 42, 46), BorderBrush = Brush(69, 69, 74),
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(2), Child = content
        };
    }

    /// <summary>Builds the centered guidance shown before the first photo is saved.</summary>
    private static Border BuildEmptyState()
    {
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = "\uE722", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 34, Foreground = Brush(110, 110, 116), HorizontalAlignment = HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = Text("RenderPhotos.EmptyState"), Foreground = Brush(174, 174, 180),
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360, Margin = new Thickness(0, 12, 0, 0)
        });
        return new Border
        {
            Background = Brush(37, 37, 40), BorderBrush = Brush(61, 61, 66), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 2), Child = content
        };
    }

    /// <summary>Updates a photo card to communicate whether it belongs to the batch.</summary>
    private static void UpdatePhotoCard(CheckBox box, Border card)
    {
        var active = box.IsChecked == true;
        card.BorderBrush = active ? Brush(22, 119, 200) : Brush(69, 69, 74);
        card.Background = active ? Brush(38, 58, 78) : Brush(42, 42, 46);
    }

    /// <summary>Wraps one output selector in a compact dark settings card.</summary>
    private static Border SelectorCard(string label, Control selector)
    {
        var content = new StackPanel();
        content.Children.Add(Label(label));
        content.Children.Add(selector);
        return new Border
        {
            Background = Brush(40, 40, 43), Padding = new Thickness(12, 8, 12, 12),
            Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(2), Child = content
        };
    }

    /// <summary>Creates a localized settings label.</summary>
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 4, 0, 5), Foreground = Brush(190, 190, 195), FontSize = 12 };

    /// <summary>Creates a compact secondary action button.</summary>
    private static Button LinkButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Creates a neutral secondary button shared by dialog actions.</summary>
    private static Button SecondaryButton(string text) => new()
    {
        Content = text, Padding = new Thickness(12, 5, 12, 5), MinHeight = 32,
        Background = Brush(52, 52, 56), Foreground = Brushes.White, BorderBrush = Brush(69, 69, 74)
    };

    /// <summary>Opens the native folder picker for the batch destination.</summary>
    private void BrowseFolder()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Text("RenderPhotos.FolderPicker"), InitialDirectory = _folder.Text
        };
        if (picker.ShowDialog(this) == true) _folder.Text = picker.FolderName;
    }

    /// <summary>Returns a frozen solid brush for stable dialog theming.</summary>
    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    /// <summary>Resolves localized user-facing copy for the batch workflow.</summary>
    private static string Text(string key) => UiText.Get(key);

    /// <summary>Selects or clears every saved photo in the batch.</summary>
    private void SetAll(bool value)
    {
        foreach (var (box, _, _) in _entries) box.IsChecked = value;
        UpdateSummary();
    }

    /// <summary>Updates the batch summary and primary action from the current selection.</summary>
    private void UpdateSummary()
    {
        var count = _entries.Count(entry => entry.Box.IsChecked == true);
        if (_entries.Count == 0)
        {
            _summary.Text = Text("RenderPhotos.Empty");
            _accept.Content = Text("RenderPhotos.RenderView");
            return;
        }
        _summary.Text = count == _entries.Count
            ? string.Format(Text("RenderPhotos.AllSummary"), count)
            : string.Format(Text("RenderPhotos.SelectionSummary"), count, _entries.Count);
        _accept.Content = count == 1
            ? Text("RenderPhotos.RenderOne")
            : string.Format(Text("RenderPhotos.RenderMany"), count);
        _accept.IsEnabled = count > 0;
    }

    /// <summary>Validates the destination and returns the immutable batch request.</summary>
    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(_folder.Text))
        {
            MessageBox.Show(this, Text("RenderPhotos.FolderRequired"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = new PhotoRenderOptions(
            [.. _entries.Where(entry => entry.Box.IsChecked == true).Select(entry => entry.Id)],
            (_size.SelectedItem as ComboBoxItem)?.Tag as string ?? "1920x1080",
            (_quality.SelectedItem as ComboBoxItem)?.Tag as string ?? "Auto",
            ((_format.SelectedItem as ComboBoxItem)?.Tag as string) == "jpg",
            _folder.Text.Trim(), _open.IsChecked == true);
        DialogResult = true;
    }
}
