using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// The fires of the render, after Lumion's fire and smoke: place a campfire or a column of smoke where the view looks and
/// choose how strong its flames and its smoke are and how tall. The window stays open beside the view; changes wait here until
/// "Aplicar" (or closing) saves them, because each save rebuilds the render scene (the flames' light is part of it).
/// </summary>
internal sealed class FireDialog : Window
{
    private readonly List<FireEmitter> _fires;
    private readonly Func<ActorPoint?> _pointUnderView;
    private readonly Action<IReadOnlyList<FireEmitter>> _apply;
    private readonly ListBox _list = new() { MinHeight = 120, MaxHeight = 190, Margin = new Thickness(0, 6, 0, 6) };
    private readonly Slider _flames = new() { Minimum = 0, Maximum = 1, TickFrequency = 0.05, IsSnapToTickEnabled = true };
    private readonly Slider _smoke = new() { Minimum = 0, Maximum = 1, TickFrequency = 0.05, IsSnapToTickEnabled = true };
    private readonly TextBox _size = new() { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MinHeight = 34 };
    private bool _showing;
    private bool _changed;

    public FireDialog(IReadOnlyList<FireEmitter> fires, Func<ActorPoint?> pointUnderView, Action<IReadOnlyList<FireEmitter>> apply)
    {
        _fires = [.. fires];
        _pointUnderView = pointUnderView;
        _apply = apply;
        Title = "Fuego y humo";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");

        var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        root.Children.Add(new TextBlock { Text = "Fuego y humo", FontSize = 18, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Mira al suelo donde quieras una fogata o una columna de humo y pulsa «Añadir…». Las llamas iluminan lo que las rodea; " +
                   "los objetos delante del fuego lo tapan. Arden en el render y en la película.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 4, 0, 6)
        });
        root.Children.Add(_list);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Button("Añadir fogata aquí", (_, _) => Add(0.9, 0.5, "Fogata")));
        row.Children.Add(Button("Añadir humo aquí", (_, _) => Add(0, 0.7, "Humo")));
        row.Children.Add(Button("Quitar", (_, _) => Remove()));
        root.Children.Add(row);

        root.Children.Add(Label("Llamas"));
        root.Children.Add(_flames);
        root.Children.Add(Label("Humo"));
        root.Children.Add(_smoke);
        root.Children.Add(Label("Altura de las llamas (m); el humo sube varias veces más"));
        root.Children.Add(_size);
        _list.SelectionChanged += (_, _) => ShowSelected();
        _flames.ValueChanged += (_, _) => Edit();
        _smoke.ValueChanged += (_, _) => Edit();
        _size.LostFocus += (_, _) => Edit();
        root.Children.Add(_message);

        var applyButton = new Button { Content = "Aplicar al render", MinWidth = 140, MinHeight = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        applyButton.Click += (_, _) => Apply();
        var close = new Button { Content = "Cerrar", MinWidth = 90, MinHeight = 32, IsCancel = true };
        close.Click += (_, _) => Close();
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0), Children = { applyButton, close } });
        Content = root;
        Closing += (_, _) => { if (_changed) Apply(); };
        Refresh(null);
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 6) };
        button.Click += click;
        return button;
    }

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 2) };

    private FireEmitter? Selected => _list.SelectedItem is ListBoxItem { Tag: Guid id } ? _fires.FirstOrDefault(fire => fire.Id == id) : null;

    private static string Percent(double value) => value.ToString("0%", CultureInfo.InvariantCulture);

    private void Refresh(Guid? select)
    {
        _list.Items.Clear();
        foreach (var fire in _fires)
        {
            var item = new ListBoxItem
            {
                Tag = fire.Id,
                Content = $"{fire.Name} · llamas {Percent(fire.Flames)} · humo {Percent(fire.Smoke)} · {fire.Size.ToString("0.0#", CultureInfo.InvariantCulture)} m"
            };
            _list.Items.Add(item);
            if (select == fire.Id) _list.SelectedItem = item;
        }
        if (_list.SelectedItem is null && _list.Items.Count > 0) _list.SelectedIndex = 0;
        ShowSelected();
    }

    private void ShowSelected()
    {
        _showing = true;
        try
        {
            var fire = Selected;
            _flames.IsEnabled = _smoke.IsEnabled = _size.IsEnabled = fire is not null;
            if (fire is null) return;
            _flames.Value = fire.Flames;
            _smoke.Value = fire.Smoke;
            _size.Text = fire.Size.ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally { _showing = false; }
    }

    private void Edit()
    {
        if (_showing || Selected is not { } fire) return;
        var size = double.TryParse(_size.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, 0.1, 10) : fire.Size;
        var edited = fire with { Flames = _flames.Value, Smoke = _smoke.Value, Size = size };
        if (edited == fire) return;
        if (!edited.IsValid) { _message.Text = "Un fuego necesita llamas, humo o ambos."; ShowSelected(); return; }
        _fires[_fires.IndexOf(fire)] = edited;
        _changed = true;
        Refresh(edited.Id);
    }

    private void Add(double flames, double smoke, string name)
    {
        if (_fires.Count >= Fires.Maximum) { _message.Text = $"Un proyecto admite hasta {Fires.Maximum} fuegos."; return; }
        if (_pointUnderView() is not { } point) { _message.Text = "Mira hacia el suelo: no hay nada bajo el centro de la vista."; return; }
        var fire = new FireEmitter(Guid.NewGuid(), $"{name} {_fires.Count + 1}", point.X, point.Y, point.Z, flames, smoke, 1.0);
        _fires.Add(fire);
        _changed = true;
        Refresh(fire.Id);
        _message.Text = $"{name} colocada. Ajusta las llamas, el humo y la altura aquí abajo.";
    }

    private void Remove()
    {
        if (Selected is not { } fire) return;
        _fires.Remove(fire);
        _changed = true;
        Refresh(null);
        _message.Text = $"«{fire.Name}» se quitará al aplicar.";
    }

    private void Apply()
    {
        _changed = false;
        _apply(_fires);
        _message.Text = _fires.Count == 0 ? "No hay fuegos en el proyecto." : $"{_fires.Count} fuego(s) aplicados: arden en el render y en la película.";
    }
}
