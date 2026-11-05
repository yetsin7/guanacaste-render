using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// Lumion-style material balls: every surface row and every library tile shows a lit sphere that answers to the
/// material's colour, texture, roughness, metalness and relief, so a material is judged by sight and not by name.
/// </summary>
public partial class RenderPanel
{
    private int _materialBallRevision;

    private static float BallLinearToSrgb(float linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        return linear <= .0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - .055f;
    }

    private static float BallSrgbToLinear(byte value)
    {
        var srgb = value / 255f;
        return srgb <= .04045f ? srgb / 12.92f : MathF.Pow((srgb + .055f) / 1.055f, 2.4f);
    }

    /// <summary>A plain-colour image the sphere shader can wrap around the ball when a material has no picture.</summary>
    private static BitmapSource SolidSwatch(Vector3 linear)
    {
        var b = (byte)Math.Round(BallLinearToSrgb(linear.Z) * 255);
        var g = (byte)Math.Round(BallLinearToSrgb(linear.Y) * 255);
        var r = (byte)Math.Round(BallLinearToSrgb(linear.X) * 255);
        var pixels = new byte[16];
        for (var i = 0; i < 4; i++) { pixels[i * 4] = b; pixels[i * 4 + 1] = g; pixels[i * 4 + 2] = r; pixels[i * 4 + 3] = 255; }
        var swatch = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        swatch.Freeze();
        return swatch;
    }

    private static Vector3 TintToLinear(string tintHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(tintHex);
        return new Vector3(BallSrgbToLinear(color.R), BallSrgbToLinear(color.G), BallSrgbToLinear(color.B));
    }

    /// <summary>The ball of a library material that has no photograph: its flat colour, glossiness and metal.</summary>
    private static BitmapSource SolidMaterialBall(RenderMaterial material, double roughness, double metallic,
        double relief, string? tintHex)
    {
        var color = tintHex is not null ? TintToLinear(tintHex) : material.BaseColor;
        // Glass has no colour of its own: show it pale and clear, as Lumion does, instead of a black ball.
        if (material.Transmission > .5f)
            color = Vector3.Lerp(new Vector3(.9f), material.TransmissionTint, .5f);
        return MaterialSpherePreview.Create(SolidSwatch(color), roughness, metallic, relief);
    }

    /// <summary>
    /// Builds the ball of one surface row. Runs on a worker: it reads the shipped texture or package picture when
    /// there is one, and falls back to the flat colour of the preset or of the stock material of that surface.
    /// </summary>
    private static BitmapSource? RenderMaterialBall(RenderMaterialAssignment? assignment, SurfaceKind category,
        string? projectFile, double roughness, double metallic, double relief)
    {
        try
        {
            var stock = MaterialLibrary.ForKind(category);
            var preset = assignment is null ? null : MaterialPresets.FromPath(assignment.TexturePath);
            var file = assignment is null || assignment.TexturePath == "stock:"
                ? null : RenderTextureLibrary.Resolve(assignment.TexturePath, projectFile);
            if (file is not null && File.Exists(file))
                return MaterialSpherePreview.Create(DecodeGalleryImage(file, 192), roughness, metallic, relief);
            return SolidMaterialBall(preset?.Material ?? stock, roughness, metallic, relief, assignment?.TintHex);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                            NotSupportedException or ArgumentException or FormatException or
                                            InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>The picture frame of a row: a dark well that fills with the ball once it has been drawn.</summary>
    private Border BuildMaterialBall(RenderMaterialAssignment? assignment, SurfaceKind category, int revision)
    {
        var original = GalleryOriginalAppearance(assignment, category);
        var roughness = assignment?.RoughnessOverride ?? original.Roughness;
        var metallic = assignment?.MetallicOverride ?? original.Metallic;
        var picture = new MaterialBall3D(roughness, metallic);
        var frame = new Border
        {
            Width = 100, Height = 64, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(24, 36, 49)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 83, 101)), BorderThickness = new Thickness(1),
            Child = picture,
            ToolTip = $"Muestra 3D: arrastra para girar · rugosidad {roughness:0.00} · metálico {metallic:0.00}"
        };
        var projectFile = _source?.ProjectFilePath;
        _ = LoadMaterialTextureAsync(picture, revision, assignment, category, projectFile);
        return frame;
    }

    private async Task LoadMaterialTextureAsync(MaterialBall3D ball, int revision,
        RenderMaterialAssignment? assignment, SurfaceKind category, string? projectFile)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                var preset = assignment is null ? null : MaterialPresets.FromPath(assignment.TexturePath);
                var file = assignment is null || assignment.TexturePath == "stock:" ? null :
                    RenderTextureLibrary.Resolve(assignment.TexturePath, projectFile);
                if (file is not null && File.Exists(file)) return DecodeGalleryImage(file, 192);
                var material = preset?.Material ?? MaterialLibrary.ForKind(category);
                var color = assignment?.TintHex is { } tint ? TintToLinear(tint) : material.BaseColor;
                if (material.Transmission > .5f) color = Vector3.Lerp(new Vector3(.9f), material.TransmissionTint, .5f);
                return SolidSwatch(color);
            });
            if (revision == _materialBallRevision) ball.SetTexture(bitmap);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                            NotSupportedException or ArgumentException or FormatException or
                                            InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    private const int OptionBallCacheLimit = 240;
    private readonly Dictionary<string, BitmapSource> _optionBalls = new(StringComparer.Ordinal);

    private static readonly Brush TileBackground = new SolidColorBrush(Color.FromRgb(31, 47, 64));
    private static readonly Brush TileSelectedBorder = new SolidColorBrush(Color.FromRgb(107, 215, 197));
    private static readonly Brush TileBorder = new SolidColorBrush(Color.FromRgb(62, 83, 101));

    private static bool IsActionTag(string? tag) =>
        tag is StockTag or CustomTag or PbrPackageTag or CreatePbrTag or BrowsePbrTag;

    /// <summary>
    /// The closed picker of a surface: a button that opens a Lumion-style grid of material balls (with a search
    /// box) instead of a plain text list. Choosing a ball selects it in the hidden picker, which applies it.
    /// </summary>
    private Button BuildMaterialPickerFace(ComboBox picker, SurfaceKind category, bool focusedHere, Action populate)
    {
        var chevron = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(179, 196, 214))
        };
        var name = new TextBlock
        {
            Text = (picker.SelectedItem as ComboBoxItem)?.Content as string ?? "",
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
        var head = new DockPanel();
        DockPanel.SetDock(chevron, Dock.Right);
        head.Children.Add(chevron);
        head.Children.Add(name);
        var face = new Button
        {
            Content = head, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0), MinHeight = 32,
            Padding = new Thickness(9, 5, 9, 5), ToolTip = "Elegir el material con bolas de ejemplo"
        };
        if (focusedHere) { face.BorderBrush = Brushes.DarkOrange; face.BorderThickness = new Thickness(2); }

        var tiles = new WrapPanel();
        // One chip per material family so a long catalogue is crossed in a click, not by scrolling.
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        string? activeFamily = null;
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 8), ToolTip = "Buscar por nombre o familia" };
        var scroller = new ScrollViewer
        {
            MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = tiles
        };
        var stack = new StackPanel();
        stack.Children.Add(search);
        stack.Children.Add(chips);
        stack.Children.Add(scroller);
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = face, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true,
            Child = new Border
            {
                Width = 408, Padding = new Thickness(10), CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromRgb(22, 33, 45)), BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(74, 104, 130)), Child = stack,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, Opacity = .5, ShadowDepth = 4 }
            }
        };

        void Rebuild()
        {
            tiles.Children.Clear();
            chips.Children.Clear();
            var families = picker.Items.OfType<ComboBoxItem>()
                .Where(entry => !entry.IsEnabled && entry.Tag is null).Select(entry => entry.Content as string ?? "").ToArray();
            if (activeFamily is not null && !families.Contains(activeFamily)) activeFamily = null;
            foreach (var family in families.Prepend(""))
            {
                var isAll = family.Length == 0;
                var on = isAll ? activeFamily is null : family == activeFamily;
                var chip = new Button
                {
                    Content = isAll ? "Todos" : family, Margin = new Thickness(0, 0, 4, 4), MinHeight = 24,
                    Padding = new Thickness(9, 2, 9, 2), FontSize = 11,
                    Background = on ? new SolidColorBrush(Color.FromRgb(36, 110, 104)) : TileBackground,
                    BorderBrush = on ? TileSelectedBorder : TileBorder
                };
                var chosenFamily = isAll ? null : family;
                chip.Click += (_, _) => { activeFamily = chosenFamily; Rebuild(); };
                chips.Children.Add(chip);
            }
            string? heading = null;
            var selectedTag = (picker.SelectedItem as ComboBoxItem)?.Tag as string;
            foreach (var item in picker.Items.OfType<ComboBoxItem>().ToArray())
            {
                var label = item.Content as string ?? "";
                if (!item.IsEnabled && item.Tag is null) heading = label;
                // With a family chosen, only its own balls remain; the import actions belong to "Todos".
                if (activeFamily is not null && (heading != activeFamily || IsActionTag(item.Tag as string))) continue;
                if (!item.IsEnabled && item.Tag is null)
                {
                    if (activeFamily is not null) continue;
                    tiles.Children.Add(new TextBlock
                    {
                        Text = label, Width = 372, Margin = new Thickness(2, 10, 2, 4), FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(107, 215, 197))
                    });
                    continue;
                }
                var tag = item.Tag as string;
                Button tile;
                if (IsActionTag(tag))
                {
                    tile = new Button
                    {
                        Content = label.Trim(), Width = 372, Margin = new Thickness(2),
                        HorizontalContentAlignment = HorizontalAlignment.Left
                    };
                }
                else
                {
                    var preset = tag is null ? null : MaterialPresets.FromPath(tag);
                    var texture = tag is null ? null : RenderTextureLibrary.BuiltIn.FirstOrDefault(entry =>
                        RenderTextureLibrary.BuiltInPrefix + entry.Key == tag);
                    tile = new Button
                    {
                        Content = BuildMaterialTileContent(label.Trim(), tag ?? "", category,
                            preset?.Material.Roughness ?? .82, preset?.Material.Metallic ?? 0,
                            preset?.Relief ?? texture?.Relief ?? .3),
                        Width = 92, Margin = new Thickness(2), Padding = new Thickness(3, 4, 3, 5),
                        Background = TileBackground, ToolTip = label.Trim(),
                        BorderBrush = tag == selectedTag ? TileSelectedBorder : TileBorder,
                        BorderThickness = new Thickness(tag == selectedTag ? 2 : 1)
                    };
                }
                var chosen = item;
                tile.Click += (_, _) =>
                {
                    popup.IsOpen = false;
                    picker.SelectedItem = chosen;
                };
                tiles.Children.Add(tile);
            }
        }

        face.Click += (_, _) =>
        {
            search.Text = MaterialSearch.Text;
            populate();
            Rebuild();
            popup.IsOpen = true;
            Dispatcher.BeginInvoke(new Action(() => search.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        };
        search.TextChanged += (_, _) =>
        {
            if (!popup.IsOpen) return;
            MaterialSearch.Text = search.Text;
            populate();
            Rebuild();
        };
        // The popup joins the row's logical tree so it inherits the panel's dark styles.
        face.Loaded += (_, _) =>
        {
            if (face.Parent is Panel owner && popup.Parent is null) owner.Children.Add(popup);
        };
        return face;
    }

    /// <summary>A ball with the material name under it; the ball is drawn when the tile first appears.</summary>
    private FrameworkElement BuildMaterialTileContent(string name, string path, SurfaceKind category,
        double roughness, double metallic, double relief)
    {
        var picture = new Image { Width = 80, Height = 51, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        var panel = new StackPanel();
        panel.Children.Add(picture);
        panel.Children.Add(new TextBlock
        {
            Text = name, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            MaxHeight = 28, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(234, 240, 246))
        });
        picture.Loaded += async (_, _) =>
        {
            if (picture.Source is not null) return;
            if (_optionBalls.TryGetValue(path, out var cached)) { picture.Source = cached; return; }
            var projectFile = _source?.ProjectFilePath;
            await _galleryDecodeGate.WaitAsync();
            BitmapSource? ball;
            try
            {
                ball = await Task.Run(() => RenderMaterialBall(new RenderMaterialAssignment(category, path),
                    category, projectFile, roughness, metallic, relief));
            }
            finally { _galleryDecodeGate.Release(); }
            if (ball is null) return;
            if (_optionBalls.Count >= OptionBallCacheLimit) _optionBalls.Clear();
            _optionBalls[path] = ball;
            picture.Source = ball;
        };
        return panel;
    }
}
