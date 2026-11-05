using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core.Rendering;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// The effect stack and picker of the Render panel, after Lumion's +FX: the effects in use are listed with an eye to switch
/// each one off and their own settings underneath, and "+ FX" opens a picker with categories and a tile per effect. The
/// stack owns no values of its own: every row edits the very sliders and checkboxes the render already reads, so the
/// preview, the final image and the saved project stay exactly as they were.
/// </summary>
public partial class RenderPanel
{
    private const long MaximumStyleBytes = 256 * 1024;
    private const string FxFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    private readonly HashSet<string> _fxActive = [];
    private readonly HashSet<string> _fxOpen = [];
    // Values a switched-off effect had, kept so the eye can bring them back.
    private readonly Dictionary<string, Dictionary<string, double>> _fxMuted = [];
    private FxCategory _fxCategory = FxCategory.Featured;
    private GridLength? _settingsWidthBeforeTheater;
    private RenderSetup? _copiedPhotoEffects;
    private double _settingsMinimumBeforeTheater;

    private static readonly (FxCategory Category, string Name, string Glyph)[] FxCategories =
    [
        (FxCategory.Featured, "Destacado", ""), (FxCategory.Sun, "Sol", ""), (FxCategory.Weather, "Clima", ""),
        (FxCategory.Sky, "Cielo", ""), (FxCategory.Objects, "Objetos", ""), (FxCategory.Camera, "Cámara", ""),
        (FxCategory.Animation, "Animación", ""), (FxCategory.Artistic1, "Artístico 1", ""),
        (FxCategory.Artistic2, "Artístico 2", ""), (FxCategory.Advanced, "Avanzado", "")
    ];

    // ---------------------------------------------------------------- the settings an effect edits

    private double? FxRead(string key) => FindName(key) switch
    {
        Slider slider => slider.Value,
        CheckBox box => box.IsChecked == true ? 1 : 0,
        ComboBox list => Math.Max(0, list.SelectedIndex),
        _ => null
    };

    private void FxWrite(string key, double value)
    {
        switch (FindName(key))
        {
            case Slider slider: slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum); break;
            case CheckBox box: box.IsChecked = value >= 0.5; break;
            case ComboBox { Items.Count: > 0 } list: list.SelectedIndex = Math.Clamp((int)Math.Round(value), 0, list.Items.Count - 1); break;
        }
    }

    private void FxResetToNeutral(FxEffect effect)
    {
        foreach (var control in effect.Controls) FxWrite(control.Key, control.Neutral);
    }

    // ---------------------------------------------------------------- the stack

    /// <summary>
    /// Rebuilds which finishing effects are in use from the controls themselves, after a project or a style set them:
    /// an effect with any setting off its neutral value is in use, and one at neutral is not (unless its eye is closed).
    /// </summary>
    private void SyncFxFromControls()
    {
        foreach (var effect in RenderFx.All.Where(item => item.Removable && !item.Planned))
        {
            if (!effect.IsNeutral(FxRead))
            {
                _fxActive.Add(effect.Id);
                _fxMuted.Remove(effect.Id);
            }
            else if (!_fxMuted.ContainsKey(effect.Id)) _fxActive.Remove(effect.Id);
        }
        RefreshFxStack();
    }

    /// <summary>Lists the effects in use, in the order the picker defines them.</summary>
    private void RefreshFxStack()
    {
        if (FxStackList is null) return;
        FxStackList.Children.Clear();
        foreach (var effect in RenderFx.All.Where(item => _fxActive.Contains(item.Id)))
            FxStackList.Children.Add(BuildFxRow(effect));
        FxEmptyHint.Visibility = _fxActive.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildFxRow(FxEffect effect)
    {
        var muted = _fxMuted.ContainsKey(effect.Id);
        var open = _fxOpen.Contains(effect.Id);
        var body = new StackPanel { Margin = new Thickness(4, 6, 4, 2), IsEnabled = !muted, Opacity = muted ? 0.5 : 1 };
        foreach (var control in effect.Controls)
            if (BuildFxControl(control) is { } element) body.Children.Add(element);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (effect.Removable)
        {
            var eye = FxIconButton(muted ? "" : "", muted ? "Activar el efecto" : "Desactivar el efecto sin quitarlo");
            eye.Click += (_, _) => ToggleFxMute(effect);
            header.Children.Add(eye);
        }
        var name = new Button
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 6, 8, 6), Cursor = Cursors.Hand, ToolTip = effect.Description,
            Content = new TextBlock
            {
                Text = effect.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(muted ? Color.FromRgb(0x8F, 0xA6, 0xB9) : Color.FromRgb(0xEA, 0xF0, 0xF6))
            }
        };
        name.Click += (_, _) =>
        {
            if (!_fxOpen.Add(effect.Id)) _fxOpen.Remove(effect.Id);
            RefreshFxStack();
        };
        Grid.SetColumn(name, 1);
        header.Children.Add(name);
        var remove = FxIconButton("", "Quitar el efecto");
        remove.Click += (_, _) => RemoveFx(effect);
        Grid.SetColumn(remove, 2);
        header.Children.Add(remove);

        var stack = new StackPanel();
        stack.Children.Add(header);
        if (open) stack.Children.Add(body);
        return new Border
        {
            Child = stack, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(6, 2, 6, 6), CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x34, 0x47)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x53, 0x65)), BorderThickness = new Thickness(1)
        };
    }

    private static Button FxIconButton(string glyph, string tip) => new()
    {
        Content = glyph, FontFamily = new FontFamily(FxFont), FontSize = 15, ToolTip = tip, Width = 34, Height = 32,
        Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
        Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xEB, 0xF5))
    };

    /// <summary>A setting of an effect, tied both ways to the panel control that holds its value.</summary>
    private UIElement? BuildFxControl(FxControl control)
    {
        switch (FindName(control.Key))
        {
            case Slider source:
            {
                var slider = new Slider
                {
                    Minimum = source.Minimum, Maximum = source.Maximum, TickFrequency = source.TickFrequency,
                    IsSnapToTickEnabled = source.IsSnapToTickEnabled
                };
                slider.SetBinding(RangeBase.ValueProperty, new Binding(nameof(Slider.Value)) { Source = source, Mode = BindingMode.TwoWay });
                var value = new TextBlock
                {
                    Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9))
                };
                value.SetBinding(TextBlock.TextProperty, new Binding(nameof(Slider.Value)) { Source = source, StringFormat = "0.##" });
                DockPanel.SetDock(value, Dock.Right);
                var row = new DockPanel();
                row.Children.Add(value);
                row.Children.Add(slider);
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                panel.Children.Add(new TextBlock { Text = control.Label, Style = (Style)FindResource("PanelLabel"), Margin = new Thickness(0, 6, 0, 2) });
                panel.Children.Add(row);
                return panel;
            }
            case ComboBox source:
            {
                // The list is the very one in the panel: same entries, same selection, so the two can never disagree.
                var list = new ComboBox { ItemsSource = source.Items };
                list.SetBinding(Selector.SelectedIndexProperty,
                    new Binding(nameof(ComboBox.SelectedIndex)) { Source = source, Mode = BindingMode.TwoWay });
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                panel.Children.Add(new TextBlock { Text = control.Label, Style = (Style)FindResource("PanelLabel"), Margin = new Thickness(0, 6, 0, 2) });
                panel.Children.Add(list);
                return panel;
            }
            case CheckBox source:
            {
                var box = new CheckBox { Content = control.Label, Style = (Style)FindResource("PanelCheck") };
                box.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = source, Mode = BindingMode.TwoWay });
                return box;
            }
            default:
                return null;
        }
    }

    /// <summary>The eye: a closed eye sets the effect's settings to neutral and keeps them, an open one puts them back.</summary>
    private void ToggleFxMute(FxEffect effect)
    {
        if (_fxMuted.Remove(effect.Id, out var kept))
        {
            foreach (var (key, value) in kept) FxWrite(key, value);
        }
        else
        {
            _fxMuted[effect.Id] = effect.Controls.ToDictionary(control => control.Key, control => FxRead(control.Key) ?? control.Neutral);
            FxResetToNeutral(effect);
        }
        RefreshFxStack();
    }

    private void RemoveFx(FxEffect effect)
    {
        // A finishing effect stops acting when it leaves the stack; a scene effect keeps the sun and sky as they are.
        if (effect.Removable && !_fxMuted.ContainsKey(effect.Id)) FxResetToNeutral(effect);
        _fxMuted.Remove(effect.Id);
        _fxActive.Remove(effect.Id);
        _fxOpen.Remove(effect.Id);
        RefreshFxStack();
    }

    private void AddFx(FxEffect effect)
    {
        if (effect.Planned)
        {
            StatusText.Text = $"«{effect.Name}» llegará en una próxima versión.";
            return;
        }
        if (_fxActive.Add(effect.Id) && effect.Removable)
            foreach (var control in effect.Controls) FxWrite(control.Key, control.StartValue);
        _fxMuted.Remove(effect.Id);
        _fxOpen.Add(effect.Id);
        RefreshFxStack();
        SectionTabs.SelectedIndex = 1;
        // Layers are placed on the movie's timeline, so adding the effect takes the person there.
        if (effect.Id == "layers")
        {
            SetMode(RenderMode.Movie);
            StatusText.Text = "Pulsa «Capas» en la barra de película y elige un nivel para que aparezca o se oculte en el momento marcado.";
        }
    }

    // ---------------------------------------------------------------- the picker

    private void AddFx_Click(object sender, RoutedEventArgs e)
    {
        _fxOpener = sender as Button;
        FxSearch.Clear();
        BuildFxCategories();
        ShowFxCategory(_fxCategory);
        FxPicker.Visibility = Visibility.Visible;
        FxSearch.Focus();
    }

    private Button? _fxOpener;

    private void CloseFxPicker()
    {
        FxPicker.Visibility = Visibility.Collapsed;
        _fxOpener?.Focus();
    }

    private void FxPickerClose_Click(object sender, RoutedEventArgs e) => CloseFxPicker();

    private void FxPicker_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        CloseFxPicker();
    }

    private void FxSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (FxTiles is not null) ShowFxCategory(_fxCategory);
    }

    private void BuildFxCategories()
    {
        FxCategoryList.Children.Clear();
        foreach (var (category, name, glyph) in FxCategories)
        {
            var selected = category == _fxCategory;
            var button = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 0, 0, 1),
                BorderThickness = new Thickness(selected ? 1 : 0), BorderBrush = Brushes.LightSteelBlue, Cursor = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF0, 0xF6)),
                Background = new SolidColorBrush(selected ? Color.FromRgb(0x32, 0x6B, 0x88) : Color.FromArgb(0, 0, 0, 0)),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = glyph, FontFamily = new FontFamily(FxFont), Width = 24, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal }
                    }
                }
            };
            var chosen = category;
            button.Click += (_, _) =>
            {
                _fxCategory = chosen;
                FxSearch.Clear();
                BuildFxCategories();
                ShowFxCategory(chosen);
            };
            FxCategoryList.Children.Add(button);
        }
    }

    private void ShowFxCategory(FxCategory category)
    {
        FxTiles.Children.Clear();
        var query = FxSearch.Text.Trim();
        var effects = query.Length == 0
            ? (category == FxCategory.Featured ? ReferenceFeaturedEffects() : RenderFx.In(category))
            : RenderFx.All.Where(effect => effect.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || effect.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        foreach (var effect in effects) FxTiles.Children.Add(BuildFxTile(effect));
    }

    private static readonly string[] ReferenceEffectIds = ["orthographic", "perspective", "dof", "matching", "clouds", "sun", "shadow", "reflection", "hyperlight", "skylight", "color", "layers"];

    private static IEnumerable<FxEffect> ReferenceFeaturedEffects()
    {
        string[] names = ["Vista ortográfica", "Perspectiva de 2 puntos", "Profundidad de campo", "Ajuste fotográfico", "Cielos reales", "Sol", "Sombras", "Reflexión", "Hyperlight", "Luz del cielo", "Corrección de color", "Visibilidad de capas"];
        for (var i = 0; i < ReferenceEffectIds.Length; i++)
            yield return RenderFx.Find(ReferenceEffectIds[i]) ?? new FxEffect(ReferenceEffectIds[i], names[i], "Este efecto de la referencia aún no está disponible en el motor.", FxCategory.Featured, "", [], Planned: true);
    }

    private static ImageSource EffectThumbnail(FxEffect effect)
    {
        var index = Array.IndexOf(ReferenceEffectIds, effect.Id);
        if (index < 0) return new BitmapImage(new Uri("pack://application:,,,/resources/samples/casa-del-cobogo.png"));
        var reference = new BitmapImage(new Uri("pack://application:,,,/resources/render-reference/effects.png"));
        // Only the illustrative photograph is used; names and state remain live controls.
        return new CroppedBitmap(reference, new Int32Rect(150 + index % 3 * 162, 63 + index / 3 * 96, 160, 57));

    }

    private UIElement BuildFxTile(FxEffect effect)
    {
        var inUse = _fxActive.Contains(effect.Id);
        var tile = new Grid { Height = 120, ClipToBounds = true };
        tile.Children.Add(new Border
        {
            Background = new ImageBrush(EffectThumbnail(effect)) { Stretch = Stretch.UniformToFill },
            Opacity = 1
        });
        tile.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(190, 0, 0, 0), Color.FromArgb(25, 0, 0, 0), 90)
        });
        tile.Children.Add(new TextBlock
        {
            Text = effect.Name, Foreground = Brushes.White, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 6, 8, 0), VerticalAlignment = VerticalAlignment.Top
        });
        if (inUse || effect.Planned)
            tile.Children.Add(new TextBlock
            {
                Text = effect.Planned ? "Próximamente" : "Ya en uso", Foreground = Brushes.White, FontSize = 10.5,
                Margin = new Thickness(0, 0, 8, 5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom
            });
        var card = new Button
        {
            Content = tile, Padding = new Thickness(0), Margin = new Thickness(0), Cursor = Cursors.Hand,
            ToolTip = effect.Description, BorderThickness = new Thickness(0, 0, 1, 1), BorderBrush = new SolidColorBrush(Color.FromRgb(0x83, 0x92, 0x97))

        };
        card.MouseEnter += (_, _) => card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7B, 0xDC, 0xD0));
        card.MouseLeave += (_, _) => card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x53, 0x65));
        System.Windows.Automation.AutomationProperties.SetName(card, effect.Name);
        card.Click += (_, _) =>
        {
            AddFx(effect);
            CloseFxPicker();
        };
        return card;
    }

    private static Color HsvColor(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - chroma;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0.0), 1 => (x, chroma, 0.0), 2 => (0.0, chroma, x),
            3 => (0.0, x, chroma), 4 => (x, 0.0, chroma), _ => (chroma, 0.0, x)
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    // ---------------------------------------------------------------- saved styles

    private void FxMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = FxMenuButton, Placement = PlacementMode.Bottom };
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }
        menu.Items.Add(Item(UiText.Get("RenderPanel.CopyEffects"), CopyPhotoEffects));
        var paste = Item(UiText.Get("RenderPanel.PasteEffects"), PastePhotoEffects);
        paste.IsEnabled = _copiedPhotoEffects is not null && _source is not null;
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Guardar estilo de efectos…", SaveFxStyle));
        menu.Items.Add(Item("Cargar estilo de efectos…", LoadFxStyle));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quitar todos los efectos", ClearFxStack));
        menu.IsOpen = true;
    }

    /// <summary>Stores the current image finish for another photo in this render session.</summary>
    private void CopyPhotoEffects()
    {
        if (_source is null) return;
        WatchPhotoEdits();
        _copiedPhotoEffects = CaptureCurrentSetup();
        StatusText.Text = UiText.Get("RenderPanel.EffectsCopied");
    }

    /// <summary>Applies copied image settings without changing the active photo's camera or sky.</summary>
    private void PastePhotoEffects()
    {
        if (_source is null || _copiedPhotoEffects is null) return;
        var setup = CaptureCurrentSetup().WithEffectsFrom(_copiedPhotoEffects);
        _applyingPhoto = true;
        try { ApplySetupForProject(setup, _source.ProjectFilePath); }
        finally { _applyingPhoto = false; }
        WatchPhotoEdits();
        StatusText.Text = UiText.Get("RenderPanel.EffectsPasted");
    }

    /// <summary>Saves the finishing effects that are on, with their settings, as a style file that can be shared.</summary>
    private void SaveFxStyle()
    {
        var effects = RenderFx.All.Where(item => item.Removable && _fxActive.Contains(item.Id) && !_fxMuted.ContainsKey(item.Id)).ToList();
        var dialog = new SaveFileDialog { Title = "Guardar estilo de efectos", Filter = "Estilo de efectos (*.g3dfx)|*.g3dfx", FileName = "Mi estilo" };
        if (dialog.ShowDialog() != true) return;
        var values = effects.SelectMany(effect => effect.Controls).Where(control => control.Kind != FxControlKind.Choice).ToDictionary(control => control.Key, control => FxRead(control.Key) ?? control.Neutral);
        var style = new FxStyle(Path.GetFileNameWithoutExtension(dialog.FileName), effects.Select(effect => effect.Id).ToList(), values);
        try
        {
            File.WriteAllText(dialog.FileName, style.ToJson());
            StatusText.Text = $"Estilo «{style.Name}» guardado.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "No se pudo guardar el estilo: " + error.Message;
        }
    }

    /// <summary>Replaces the stack with a saved style; only settings of the effects the file lists are applied.</summary>
    private void LoadFxStyle()
    {
        var dialog = new OpenFileDialog { Title = "Cargar estilo de efectos", Filter = "Estilo de efectos (*.g3dfx)|*.g3dfx" };
        if (dialog.ShowDialog() != true) return;
        FxStyle? style;
        try
        {
            style = new FileInfo(dialog.FileName).Length > MaximumStyleBytes ? null : FxStyle.TryParse(File.ReadAllText(dialog.FileName));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "No se pudo leer el estilo: " + error.Message;
            return;
        }
        if (style is null)
        {
            StatusText.Text = "El archivo no es un estilo de efectos válido.";
            return;
        }
        ClearFxStack();
        foreach (var effect in style.Effects.Select(RenderFx.Find).OfType<FxEffect>().Where(item => item.Removable))
        {
            _fxActive.Add(effect.Id);
            foreach (var control in effect.Controls)
                if (control.Kind != FxControlKind.Choice && style.Values.TryGetValue(control.Key, out var value)) FxWrite(control.Key, value);
        }
        RefreshFxStack();
        StatusText.Text = $"Estilo «{style.Name}» aplicado.";
    }

    /// <summary>Takes every effect out of the stack, finishing effects back to neutral.</summary>
    private void ClearFxStack()
    {
        foreach (var effect in RenderFx.All.Where(item => item.Removable && !item.Planned)) FxResetToNeutral(effect);
        _fxActive.Clear();
        _fxMuted.Clear();
        _fxOpen.Clear();
        RefreshFxStack();
    }

    // ---------------------------------------------------------------- theater mode

    /// <summary>Hides the settings so only the picture shows; the same button brings them back.</summary>
    private void Theater_Click(object sender, RoutedEventArgs e)
    {
        var entering = _settingsWidthBeforeTheater is null;
        if (entering)
        {
            _settingsWidthBeforeTheater = SettingsColumn.Width;
            _settingsMinimumBeforeTheater = SettingsColumn.MinWidth;
            SettingsColumn.MinWidth = 0;
            SettingsColumn.Width = new GridLength(0);
        }
        else
        {
            SettingsColumn.MinWidth = _settingsMinimumBeforeTheater;
            SettingsColumn.Width = _settingsWidthBeforeTheater!.Value;
            _settingsWidthBeforeTheater = null;
        }
        var shown = entering ? Visibility.Collapsed : Visibility.Visible;
        SettingsSplitter.Visibility = shown;
        ModeOverlayHost.Visibility = shown;
        PhotoStrip.Visibility = !entering && _mode == RenderMode.Photo ? Visibility.Visible : Visibility.Collapsed;
        OutputModeHost.IsEnabled = !entering;
        TheaterText.Text = entering ? "Mostrar paneles" : "Teatro";
    }
}
