using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>
/// The people and cars of the render, after Lumion's animated people and vehicles: place one where the view is looking, add
/// the points of the route it follows, choose its look, speed and the second it sets off. The window stays open beside the view, so the camera
/// can be moved between points; changes wait here until "Aplicar" (or closing) saves them, because each save rebuilds the
/// render scene.
/// </summary>
internal sealed class PeopleDialog : Window
{
    private readonly List<Actor> _actors;
    private readonly Func<ActorPoint?> _pointUnderView;
    private readonly Action<IReadOnlyList<Actor>> _apply;
    private readonly Func<ActorPoint, ActorScatter.GroundAt> _groundNear;
    private readonly TextBox _scatterCount = new() { MinHeight = 28, Width = 60, Text = "20", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _scatterRadius = new() { MinHeight = 28, Width = 60, Text = "10", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _scatterWalk = new() { Content = "Cada uno con su propia ruta (caminan o circulan)", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ListBox _list = new() { MinHeight = 130, MaxHeight = 200, Margin = new Thickness(0, 6, 0, 6) };
    private readonly ComboBox _look = new() { MinHeight = 28 };
    private readonly TextBox _speed = new() { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _start = new() { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _loop = new() { Content = "Va y vuelve por la ruta", Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), MinHeight = 34 };
    private bool _showing;
    private bool _changed;

    public PeopleDialog(IReadOnlyList<Actor> actors, Func<ActorPoint?> pointUnderView, Action<IReadOnlyList<Actor>> apply,
        Func<ActorPoint, ActorScatter.GroundAt>? groundNear = null)
    {
        _groundNear = groundNear ?? (centre => (_, _) => centre.Z);
        _actors = [.. actors];
        _pointUnderView = pointUnderView;
        _apply = apply;
        Title = "Personas y coches";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 820;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");

        var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        root.Children.Add(new TextBlock { Text = "Personas y coches", FontSize = 18, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Mira al suelo donde quieras una persona o un coche y pulsa «Añadir…». Mueve la vista y pulsa «Añadir punto de ruta» para " +
                   "que vaya hasta allí. Se ven en el render y se mueven en la película.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 4, 0, 6)
        });
        root.Children.Add(_list);
        root.Children.Add(Row(Button("Añadir persona aquí", (_, _) => AddActor(ActorKind.Person)), Button("Añadir coche aquí", (_, _) => AddActor(ActorKind.Car))));
        root.Children.Add(Row(Button("Añadir punto de ruta", AddPoint), Button("Quitar último punto", RemovePoint), Button("Quitar", RemovePerson)));

        root.Children.Add(new TextBlock { Text = "Esparcir en masa", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        root.Children.Add(new TextBlock
        {
            Text = "Reparte muchos de una vez alrededor del punto que mira la vista, sobre el suelo, cada uno con su aspecto, su velocidad y su momento de salida.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(Row(new TextBlock { Text = "Cuántos ", VerticalAlignment = VerticalAlignment.Center }, _scatterCount,
            new TextBlock { Text = "   Radio (m) ", VerticalAlignment = VerticalAlignment.Center }, _scatterRadius));
        root.Children.Add(_scatterWalk);
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0),
            Children = { Button("Esparcir personas aquí", (_, _) => ScatterActors(ActorKind.Person)), Button("Esparcir coches aquí", (_, _) => ScatterActors(ActorKind.Car)) } });

        var form = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new (string Label, FrameworkElement Field)[] { ("Aspecto o modelo", _look), ("Velocidad (m/s)", _speed), ("Sale en el segundo", _start) };
        for (var row = 0; row < rows.Length; row++)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = rows[row].Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
            Grid.SetRow(label, row);
            Grid.SetRow(rows[row].Field, row);
            Grid.SetColumn(rows[row].Field, 1);
            rows[row].Field.Margin = new Thickness(0, 3, 0, 3);
            form.Children.Add(label);
            form.Children.Add(rows[row].Field);
        }
        root.Children.Add(form);
        root.Children.Add(_loop);
        _list.SelectionChanged += (_, _) => ShowSelected();
        _look.SelectionChanged += (_, _) => EditSelected();
        _speed.LostFocus += (_, _) => EditSelected();
        _start.LostFocus += (_, _) => EditSelected();
        _loop.Checked += (_, _) => EditSelected();
        _loop.Unchecked += (_, _) => EditSelected();
        root.Children.Add(_message);

        var applyButton = new Button { Content = "Aplicar al render", MinWidth = 140, MinHeight = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        applyButton.Click += (_, _) => Apply();
        var close = new Button { Content = "Cerrar", MinWidth = 90, MinHeight = 32, IsCancel = true };
        close.Click += (_, _) => Close();
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0), Children = { applyButton, close } });
        Content = root;
        Closing += (_, _) => { if (_changed) Apply(); };
        RefreshList(null);
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 6) };
        button.Click += click;
        return button;
    }

    private static StackPanel Row(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items) row.Children.Add(item);
        return row;
    }

    private Actor? Selected => _list.SelectedItem is ListBoxItem { Tag: Guid id } ? _actors.FirstOrDefault(actor => actor.Id == id) : null;

    private void RefreshList(Guid? select)
    {
        _list.Items.Clear();
        foreach (var actor in _actors)
        {
            var item = new ListBoxItem
            {
                Tag = actor.Id,
                Content = $"{actor.Name} · {LookName(actor.Kind, actor.Look)} · {actor.Speed.ToString("0.0", CultureInfo.InvariantCulture)} m/s · " +
                          (actor.Route.Count == 1 ? "parado" : $"{actor.Route.Count} puntos")
            };
            _list.Items.Add(item);
            if (select == actor.Id) _list.SelectedItem = item;
        }
        if (_list.SelectedItem is null && _list.Items.Count > 0) _list.SelectedIndex = 0;
        ShowSelected();
    }

    private void ShowSelected()
    {
        _showing = true;
        try
        {
            var actor = Selected;
            _look.IsEnabled = _speed.IsEnabled = _start.IsEnabled = _loop.IsEnabled = actor is not null;
            if (actor is null) return;
            // The list of looks is the person's or the car's, whichever is selected.
            _look.Items.Clear();
            for (var look = 0; look < Actor.Looks; look++) _look.Items.Add(LookName(actor.Kind, look));
            _look.SelectedIndex = actor.Look;
            _speed.Text = actor.Speed.ToString("0.##", CultureInfo.InvariantCulture);
            _start.Text = actor.Start.ToString("0.##", CultureInfo.InvariantCulture);
            _loop.IsChecked = actor.Loop;
        }
        finally { _showing = false; }
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private void EditSelected()
    {
        if (_showing || Selected is not { } actor) return;
        var edited = actor with
        {
            Look = Math.Clamp(_look.SelectedIndex, 0, Actor.Looks - 1),
            Speed = TryNumber(_speed.Text, out var speed) ? Math.Clamp(speed, actor.Kind == ActorKind.Car ? 0.5 : 0.1, actor.Kind == ActorKind.Car ? Actor.MaximumSpeed : 5) : actor.Speed,
            Start = TryNumber(_start.Text, out var start) ? Math.Clamp(start, 0, 36000) : actor.Start,
            Loop = _loop.IsChecked == true
        };
        if (edited == actor) return;
        _actors[_actors.IndexOf(actor)] = edited;
        _changed = true;
        RefreshList(edited.Id);
    }

    private static string LookName(ActorKind kind, int look) => kind == ActorKind.Car
        ? new[] { "Sedán gris claro", "Sedán negro", "Sedán rojo", "Coupé plateado", "Coupé azul", "Sedán verde" }[Math.Clamp(look, 0, Actor.Looks - 1)]
        : $"Aspecto {look + 1}";

    private void AddActor(ActorKind kind)
    {
        if (_actors.Count >= Actors.Maximum) { _message.Text = $"Un proyecto admite hasta {Actors.Maximum} personas y coches."; return; }
        if (_pointUnderView() is not { } point) { _message.Text = "Mira hacia el suelo: no hay nada bajo el centro de la vista."; return; }
        var car = kind == ActorKind.Car;
        var actor = new Actor(Guid.NewGuid(), $"{(car ? "Coche" : "Persona")} {_actors.Count + 1}", _actors.Count % Actor.Looks, car ? 8 : 1.2, true, 0, [point], kind);
        _actors.Add(actor);
        _changed = true;
        RefreshList(actor.Id);
        _message.Text = car ? "Coche colocado. Mueve la vista y añade puntos de ruta para que circule (8 m/s son unos 30 km/h)."
                            : "Persona colocada. Mueve la vista y añade puntos de ruta para que camine.";
    }

    private int _scatterRound;

    /// <summary>Puts a crowd (or a line of traffic) round the point the view looks at, on the ground there.</summary>
    private void ScatterActors(ActorKind kind)
    {
        if (!int.TryParse(_scatterCount.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 1 ||
            !TryNumber(_scatterRadius.Text, out var radius) || radius < 1)
        { _message.Text = "Escribe cuántos (1 o más) y un radio de 1 m o más."; return; }
        if (_pointUnderView() is not { } centre) { _message.Text = "Mira hacia el suelo: no hay nada bajo el centro de la vista."; return; }
        var room = Actors.Maximum - _actors.Count;
        if (room < 1) { _message.Text = $"Un proyecto admite hasta {Actors.Maximum} personas y coches."; return; }
        var wanted = Math.Min(Math.Min(count, ActorScatter.MaximumPerBatch), room);
        var made = ActorScatter.Scatter(centre, radius, wanted, kind, _scatterWalk.IsChecked == true, Environment.TickCount + _scatterRound++, _groundNear(centre), _actors.Count + 1);
        if (made.Count == 0) { _message.Text = "No encontré suelo donde esparcirlos: mira un suelo abierto o agranda el radio."; return; }
        _actors.AddRange(made);
        _changed = true;
        RefreshList(made[0].Id);
        var noun = kind == ActorKind.Car ? "coches" : "personas";
        _message.Text = made.Count < count ? $"{made.Count} {noun} esparcidos (no cabían más en ese suelo o en el límite del proyecto)." : $"{made.Count} {noun} esparcidos.";
    }

    private void AddPoint(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } actor) { _message.Text = "Elige primero una persona de la lista."; return; }
        if (actor.Route.Count >= Actor.MaximumRoutePoints) { _message.Text = "La ruta ya tiene el máximo de puntos."; return; }
        if (_pointUnderView() is not { } point) { _message.Text = "Mira hacia el suelo: no hay nada bajo el centro de la vista."; return; }
        _actors[_actors.IndexOf(actor)] = actor with { Route = [.. actor.Route, point] };
        _changed = true;
        RefreshList(actor.Id);
        _message.Text = "Punto añadido a la ruta.";
    }

    private void RemovePoint(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } actor || actor.Route.Count <= 1) { _message.Text = "Una persona necesita al menos un punto."; return; }
        _actors[_actors.IndexOf(actor)] = actor with { Route = [.. actor.Route.Take(actor.Route.Count - 1)] };
        _changed = true;
        RefreshList(actor.Id);
    }

    private void RemovePerson(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } actor) return;
        _actors.Remove(actor);
        _changed = true;
        RefreshList(null);
        _message.Text = $"«{actor.Name}» se quitará al aplicar.";
    }

    private void Apply()
    {
        _changed = false;
        _apply(_actors);
        _message.Text = _actors.Count == 0 ? "No hay personas ni coches en el proyecto." : $"{_actors.Count} aplicados: se ven en el render y se mueven en la película.";
    }
}
