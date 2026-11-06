using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// "Árboles y rocas" of the Render tab, after Lumion's brush: pick a tree, plant or rock, say how far to spread it and how close
/// together, and they are scattered in a circle round the point the view looks at, no two closer than the separation, each turned
/// and sized a little differently. It is one undoable step; the window stays open so the view can be moved and the brush used again.
/// </summary>
internal sealed class ScatterDialog : Window
{
    private readonly IReadOnlyList<MassPlacementFamily> _families;
    private readonly Func<ActorPoint?> _pointUnderView;
    private readonly Func<MassPlacementFamily, IReadOnlyList<ComponentPlacement>, double, string> _place;
    private readonly ComboBox _family = new() { MinHeight = 30 };
    private readonly ComboBox _layout = new() { MinHeight = 32 };
    private readonly Slider _direction = new() { Minimum = 0, Maximum = 360, Value = 0, TickFrequency = 15 };
    private readonly CheckBox _randomRotation = new() { Content = "Dirección aleatoria", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _extentLabel = Label("Radio del grupo (m)");
    private readonly TextBox _radius = new() { MinHeight = 30, Text = "12", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _spacing = new() { MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Slider _variation = new() { Minimum = 0, Maximum = 50, Value = 20, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock _variationText = new() { Opacity = 0.75, FontSize = 12 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), MinHeight = 36 };
    private int _seed = Environment.TickCount;

    public ScatterDialog(IReadOnlyList<MassPlacementFamily> families, Func<ActorPoint?> pointUnderView,
        Func<MassPlacementFamily, IReadOnlyList<ComponentPlacement>, double, string> place)
    {
        _families = families;
        _pointUnderView = pointUnderView;
        _place = place;
        Title = "Colocación de objetos";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");

        var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        root.Children.Add(new TextBlock { Text = "Colocación de objetos", FontSize = 18, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Mira al suelo donde quieras colocar los objetos. Elige grupo o línea y ajusta la distribución. Toda la colocación se deshace con Ctrl+Z.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 4, 0, 8)
        });
        root.Children.Add(Label("Qué esparcir"));
        foreach (var family in families) _family.Items.Add(family.Label);
        _family.SelectedIndex = families.Count > 0 ? 0 : -1;
        _family.SelectionChanged += (_, _) => SuggestSpacing();
        root.Children.Add(_family);
        root.Children.Add(Label("Modo de colocación"));
        _layout.Items.Add("Grupo · área circular");
        _layout.Items.Add("Línea · separación uniforme");
        _layout.SelectedIndex = 0;
        _layout.SelectionChanged += (_, _) => _extentLabel.Text = _layout.SelectedIndex == 1 ? "Longitud de la línea (m)" : "Radio del grupo (m)";
        root.Children.Add(_layout);
        root.Children.Add(_extentLabel);
        root.Children.Add(_radius);
        root.Children.Add(Label("Separación entre uno y otro (m)"));
        root.Children.Add(_spacing);
        root.Children.Add(Label("Dirección de la línea (°)"));
        var directionText = new TextBlock { Text = "0°", Opacity = .75 };
        _direction.ValueChanged += (_, _) => directionText.Text = $"{_direction.Value:0}°";
        root.Children.Add(_direction);
        root.Children.Add(directionText);
        root.Children.Add(_randomRotation);
        root.Children.Add(Label("Variación de tamaño"));
        _variation.ValueChanged += (_, _) => _variationText.Text = $"±{_variation.Value:0} %";
        _variationText.Text = $"±{_variation.Value:0} %";
        root.Children.Add(_variation);
        root.Children.Add(_variationText);
        root.Children.Add(_message);
        var spread = new Button { Content = "Esparcir aquí", MinWidth = 130, MinHeight = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        spread.Click += (_, _) => Spread();
        var close = new Button { Content = "Cerrar", MinWidth = 90, MinHeight = 32, IsCancel = true };
        close.Click += (_, _) => Close();
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0),
            Children = { spread, close }
        });
        Content = root;
        SuggestSpacing();
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };

    private void SuggestSpacing()
    {
        if (_family.SelectedIndex >= 0)
            _spacing.Text = _families[_family.SelectedIndex].SuggestedSpacing.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.').Replace("m", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private void Spread()
    {
        if (_family.SelectedIndex < 0) { _message.Text = "Elige qué esparcir."; return; }
        if (!TryNumber(_radius.Text, out var radius) || radius is < 1 or > 500) { _message.Text = "Escribe un radio de 1 a 500 metros."; return; }
        if (!TryNumber(_spacing.Text, out var spacing) || spacing is < 0.1 or > 500) { _message.Text = "Escribe la separación en metros, de 0,1 a 500."; return; }
        if (_pointUnderView() is not { } centre) { _message.Text = "Mira hacia el suelo: no hay nada bajo el centro de la vista."; return; }
        try
        {
            var options = new MassPlacementOptions
            {
                Spacing = spacing, ScaleVariation = _variation.Value / 100, RandomRotation = _randomRotation.IsChecked == true, Seed = _seed++
            };
            var radians = _direction.Value * Math.PI / 180;
            var dx = Math.Cos(radians) * radius / 2;
            var dy = Math.Sin(radians) * radius / 2;
            var placements = _layout.SelectedIndex == 1
                ? MassPlacement.AlongPath([new(centre.X - dx, centre.Y - dy), new(centre.X + dx, centre.Y + dy)], options)
                : MassPlacement.InArea(Circle(centre, radius), options);
            _message.Text = placements.Count == 0 ? "No cupo ninguno: agranda el radio o acerca la separación." :
                _place(_families[_family.SelectedIndex], placements, centre.Z);
        }
        catch (InvalidDataException exception)
        {
            _message.Text = exception.Message.Contains("too many", StringComparison.OrdinalIgnoreCase)
                ? "Serían demasiados (el límite es 2000 a la vez): agranda la separación o achica el radio." : LocalizedErrors.Translate(exception.Message);
        }
    }

    /// <summary>A 28-sided polygon that stands for the circle, for the area scatter.</summary>
    private static List<Point2> Circle(ActorPoint centre, double radius) =>
        Enumerable.Range(0, 28).Select(step => new Point2(centre.X + radius * Math.Cos(step * Math.PI * 2 / 28), centre.Y + radius * Math.Sin(step * Math.PI * 2 / 28))).ToList();
}
