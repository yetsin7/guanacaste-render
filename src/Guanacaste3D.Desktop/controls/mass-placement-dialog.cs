using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>A family the mass placement can spread: one loaded in the project, or a shipped .g3df not loaded yet.</summary>
internal sealed record MassPlacementFamily(string Label, Guid? LoadedId, string? LibraryPath, double SuggestedSpacing);

/// <summary>What the user chose: the family, whether to fill an area or follow a line, and how to spread it.</summary>
internal sealed record MassPlacementRequest(MassPlacementFamily Family, bool Area, MassPlacementOptions Options);

/// <summary>
/// Lumion's mass placement: pick a tree, plant or car, then either a line (a row of street trees, cars along a curb)
/// or an area to fill (a grove, a flower bed), the distance between them and how much they vary.
/// </summary>
internal sealed class MassPlacementDialog : Window
{
    /// <summary>Spanish names and a natural spacing (meters) for the families shipped in resources/families.</summary>
    private static readonly Dictionary<string, (string Label, double Spacing)> Library = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jacaranda-tree"] = ("Jacaranda", 8), ["island-tree-a"] = ("Árbol tropical A", 7),
        ["island-tree-b"] = ("Árbol tropical B", 7), ["small-tree"] = ("Árbol pequeño", 4.5),
        ["searsia"] = ("Searsia", 3.5), ["pachira"] = ("Pachira", 2), ["shrub-dense-a"] = ("Arbusto denso A", 1.6),
        ["shrub-dense-b"] = ("Arbusto denso B", 1.6), ["fern"] = ("Helecho", 1), ["calathea"] = ("Calathea", 0.8),
        ["anthurium"] = ("Anturio", 0.7), ["grass-tuft"] = ("Mata de zacate", 0.45),
        ["rock-small"] = ("Piedra pequeña", 0.8), ["rock-medium"] = ("Roca mediana", 1.6), ["rock-large"] = ("Roca grande", 2.6), ["rock-flat"] = ("Roca plana", 2.2),
        ["sedan-car"] = ("Sedán", 5.6), ["coupe-car-silver"] = ("Auto coupé plateado", 5.4)
    };

    private readonly ComboBox _family = new() { MinHeight = 30 };
    private readonly RadioButton _line = new() { Content = "A lo largo de una línea (fila de árboles, autos en la calle)", IsChecked = true };
    private readonly RadioButton _area = new() { Content = "Dispersos en un área (bosque, jardín, cantero)", Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox _spacing = new() { MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Slider _variation = new() { Minimum = 0, Maximum = 50, Value = 15, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock _variationText = new() { Opacity = 0.75, FontSize = 12 };
    private readonly CheckBox _randomTurn = new() { Content = "Girar cada uno al azar (desmárcalo para que miren a lo largo de la línea)", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.IndianRed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly IReadOnlyList<MassPlacementFamily> _families;
    private int _seed = Environment.TickCount;

    public MassPlacementRequest? Result { get; private set; }

    public MassPlacementDialog(IReadOnlyList<MassPlacementFamily> families)
    {
        _families = families;
        Title = "Colocación masiva";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        root.Children.Add(new TextBlock { Text = "Colocación masiva", FontSize = 18, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Elige qué colocar y cómo repartirlo. Luego dibuja en la planta: haz clic en cada punto y termina con doble clic o clic derecho.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 12)
        });
        root.Children.Add(Label("Familia"));
        foreach (var family in families) _family.Items.Add(family.Label);
        _family.SelectionChanged += (_, _) => SuggestSpacing();
        root.Children.Add(_family);
        root.Children.Add(Label("Distribución"));
        root.Children.Add(_line);
        root.Children.Add(_area);
        root.Children.Add(Label("Separación entre elementos (m)"));
        root.Children.Add(_spacing);
        root.Children.Add(Label("Variación de tamaño"));
        _variation.ValueChanged += (_, _) => _variationText.Text = $"± {_variation.Value:0} %";
        root.Children.Add(_variation);
        root.Children.Add(_variationText);
        root.Children.Add(_randomTurn);
        var reshuffle = new Button { Content = "Otra distribución al azar", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 10, 0, 0) };
        reshuffle.Click += (_, _) =>
        {
            _seed = unchecked(_seed * 1103515245 + 12345);
            _error.Text = "Se usará una distribución distinta.";
        };
        root.Children.Add(reshuffle);
        root.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var accept = new Button { Content = "Dibujar en la planta", MinWidth = 150, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0), IsDefault = true, FontWeight = FontWeights.SemiBold };
        var cancel = new Button { Content = "Cancelar", MinWidth = 100, MinHeight = 34, IsCancel = true };
        accept.Click += (_, _) => Accept();
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        _variationText.Text = $"± {_variation.Value:0} %";
        if (families.Count > 0) _family.SelectedIndex = 0;
    }

    /// <summary>The project's loaded mesh families first, then shipped ones that are not loaded yet.</summary>
    public static IReadOnlyList<MassPlacementFamily> Families(BuildingDocument document, string libraryFolder)
    {
        var families = document.ComponentFamilies.Where(family => family.Mesh is not null)
            .Select(family =>
            {
                var size = family.Mesh!.Decode().Size;
                return new MassPlacementFamily(family.Name, family.Id, null, Math.Round(Math.Max(0.4, Math.Max(size.X, size.Y) * 1.2), 1));
            })
            .ToList();
        if (!Directory.Exists(libraryFolder)) return families;
        foreach (var path in Directory.EnumerateFiles(libraryFolder, "*.g3df").Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!NativeDocumentStore.TryInspect(path, out var info) || info is null ||
                document.ComponentFamilies.Any(family => family.Id == info.DocumentId)) continue;
            var stem = Path.GetFileNameWithoutExtension(path);
            var (label, spacing) = Library.TryGetValue(stem, out var known) ? known : (stem, 3.0);
            families.Add(new MassPlacementFamily(label + " (biblioteca)", null, path, spacing));
        }
        return families;
    }

    private void SuggestSpacing()
    {
        if (_family.SelectedIndex >= 0)
            _spacing.Text = _families[_family.SelectedIndex].SuggestedSpacing.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private void Accept()
    {
        if (_family.SelectedIndex < 0) { _error.Text = "Elige una familia."; return; }
        var text = _spacing.Text.Replace(" m", string.Empty).Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var spacing) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out spacing))
        {
            _error.Text = "Escribe la separación en metros, por ejemplo 4.";
            return;
        }
        var options = new MassPlacementOptions
        {
            Spacing = spacing, ScaleVariation = _variation.Value / 100, RandomRotation = _randomTurn.IsChecked == true, Seed = _seed
        };
        try { options.Validate(); }
        catch (InvalidDataException exception)
        {
            _error.Text = LocalizedErrors.Translate(exception.Message);
            return;
        }
        Result = new MassPlacementRequest(_families[_family.SelectedIndex], _area.IsChecked == true, options);
        DialogResult = true;
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
}
