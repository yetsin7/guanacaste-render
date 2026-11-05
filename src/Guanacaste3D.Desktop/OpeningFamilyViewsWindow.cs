using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

/// <summary>
/// What opens when a door or window is double-clicked, as in a family editor: its parameters on the left, and beside
/// them an editable plan view and a 3D view of the element alone in a short piece of wall. Every edit redraws both views
/// at once; Apply writes the type back to the project as one undoable step.
/// </summary>
internal sealed class OpeningFamilyViewsWindow : Window
{
    private readonly DocumentSession _session;
    private readonly WallOpening _opening;
    private readonly Wall _host;
    private readonly OpeningType _type;
    private readonly TextBox _name = new(), _width = new(), _height = new(), _sill = new(), _frame = new(), _material = new(),
        _lights = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Canvas _plan = new() { ClipToBounds = true, Background = Brushes.White };
    private readonly Viewport3D _viewport = new() { ClipToBounds = true };
    private readonly Border _surface = new() { Background = Brushes.White, ClipToBounds = true };
    private readonly NavigationCamera _camera = new();
    private readonly SceneNavigator _navigator;
    private bool _flipHand, _flipFacing, _ready;
    private Vector3 _min = new(-1, -1, 0), _max = new(1, 1, 2);
    private (double Begin, double End, double Span, double Thickness, OpeningType Type)? _drawn;

    /// <summary>True when Apply changed the project.</summary>
    public bool Changed { get; private set; }

    public OpeningFamilyViewsWindow(DocumentSession session, Guid openingId)
    {
        _session = session;
        _opening = session.Document.Openings.Single(item => item.Id == openingId);
        _host = session.Document.Walls.Single(item => item.Id == _opening.WallId);
        _type = session.Document.OpeningTypes.Single(item => item.Id == _opening.OpeningTypeId);
        _flipHand = _opening.FlipHand;
        _flipFacing = _opening.FlipFacing;
        Title = string.Format(CultureInfo.CurrentCulture, Text("OpeningViews.Title"), _type.DisplayName);
        Width = 1180;
        Height = 700;
        MinWidth = 860;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");

        _name.Text = _type.Name;
        _width.Text = Format(_type.Width);
        _height.Text = Format(_type.Height);
        _sill.Text = Format(_opening.Sill);
        _frame.Text = Format(_type.FrameWidth);
        _material.Text = _type.MaterialName;
        _lights.Text = _type.GlazingLightCount.ToString(CultureInfo.CurrentCulture);
        _sill.IsEnabled = _opening.Kind == OpeningKind.Window;
        _lights.IsEnabled = _opening.Kind == OpeningKind.Window;
        foreach (var box in new[] { _name, _width, _height, _sill, _frame, _material, _lights })
            box.TextChanged += (_, _) => Redraw();

        _surface.Child = _viewport;
        _navigator = new SceneNavigator(_surface, _camera) { Bounds = () => (_min, _max) };
        _navigator.Changed += UpdateCamera;
        _surface.SizeChanged += (_, _) => UpdateCamera();
        _plan.SizeChanged += (_, _) => DrawPlan();

        Content = BuildLayout();
        Loaded += (_, _) =>
        {
            _ready = true;
            Redraw(frame: true);
        };
    }

    private UIElement BuildLayout()
    {
        var form = new StackPanel { Margin = new Thickness(12) };
        form.Children.Add(new TextBlock { Text = _type.FamilyName, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        AddField(form, "OpeningViews.Name", _name);
        AddField(form, "OpeningViews.Width", _width);
        AddField(form, "OpeningViews.Height", _height);
        if (_opening.Kind == OpeningKind.Window) AddField(form, "OpeningViews.Sill", _sill);
        AddField(form, "OpeningViews.Frame", _frame);
        if (_opening.Kind == OpeningKind.Window) AddField(form, "OpeningViews.Lights", _lights);
        AddField(form, "OpeningViews.Material", _material);

        if (_opening.Kind == OpeningKind.Door)
        {
            var hand = new Button { Content = Text("OpeningViews.FlipHand"), Margin = new Thickness(0, 12, 6, 0), Padding = new Thickness(8, 4, 8, 4) };
            var facing = new Button { Content = Text("OpeningViews.FlipFacing"), Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            hand.Click += (_, _) => { _flipHand = !_flipHand; Redraw(); };
            facing.Click += (_, _) => { _flipFacing = !_flipFacing; Redraw(); };
            var flips = new StackPanel { Orientation = Orientation.Horizontal };
            flips.Children.Add(hand);
            flips.Children.Add(facing);
            form.Children.Add(flips);
        }
        form.Children.Add(_message);

        var apply = new Button { Content = Text("OpeningViews.Apply"), MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var close = new Button { Content = Text("Parameters.Close"), MinWidth = 90, IsCancel = true };
        apply.Click += (_, _) => Apply();
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        buttons.Children.Add(apply);
        buttons.Children.Add(close);

        var left = new DockPanel { Background = Brushes.WhiteSmoke };
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        left.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var views = new Grid();
        views.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        views.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
        views.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Silver };
        Grid.SetRow(splitter, 1);
        var plan = Captioned(_plan, "OpeningViews.Plan");
        var model = Captioned(_surface, "OpeningViews.Model");
        Grid.SetRow(model, 2);
        views.Children.Add(plan);
        views.Children.Add(splitter);
        views.Children.Add(model);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(views, 1);
        root.Children.Add(left);
        root.Children.Add(views);
        return root;
    }

    private static Grid Captioned(UIElement view, string captionKey)
    {
        var grid = new Grid();
        grid.Children.Add(view);
        grid.Children.Add(new TextBlock
        {
            Text = Text(captionKey), Margin = new Thickness(8, 6, 0, 0), FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DimGray, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        });
        return grid;
    }

    private static void AddField(Panel panel, string labelKey, TextBox box)
    {
        panel.Children.Add(new TextBlock { Text = Text(labelKey), Margin = new Thickness(0, 6, 0, 2) });
        panel.Children.Add(box);
    }

    // ---------------------------------------------------------------- reading and previewing

    private bool TryReadNumber(TextBox box, out double value) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);

    private bool TryRead(out OpeningType type, out double sill)
    {
        type = _type;
        sill = 0;
        if (!TryReadNumber(_width, out var width) || !TryReadNumber(_height, out var height) ||
            !TryReadNumber(_frame, out var frame) || !int.TryParse(_lights.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var lights) ||
            (_opening.Kind == OpeningKind.Window && !TryReadNumber(_sill, out sill)) || string.IsNullOrWhiteSpace(_name.Text))
            return false;
        type = _type with
        {
            Name = _name.Text.Trim(), Width = width, Height = height, FrameWidth = frame,
            GlazingLightCount = _opening.Kind == OpeningKind.Window ? lights : 0, MaterialName = _material.Text.Trim(),
            DefaultSill = _opening.Kind == OpeningKind.Window ? sill : 0
        };
        return true;
    }

    /// <summary>Rebuilds both views from the values in the form, keeping the last good picture while they are invalid.</summary>
    private void Redraw(bool frame = false)
    {
        if (!_ready) return;
        if (!TryRead(out var type, out var sill))
        {
            _message.Text = Text("OpeningViews.Invalid");
            return;
        }
        try
        {
            var preview = BuildingDocument.Create("Preview");
            var span = Math.Max(3.0, type.Width + 1.6);
            var level = preview.Levels[0].Id;
            var wallType = _session.Document.WallTypes.FirstOrDefault(item => item.Id == _host.WallTypeId);
            if (wallType is not null && preview.WallTypes.All(item => item.Id != wallType.Id)) preview.WallTypes.Add(wallType);
            var wall = new Wall(Guid.NewGuid(), level, new Point2(0, 0), new Point2(span, 0), _host.Thickness,
                sill + type.Height + 0.5)
            { WallTypeId = wallType?.Id ?? BuiltInWallTypes.Basic200Id };
            preview.Walls.Add(wall);
            preview.OpeningTypes.RemoveAll(item => item.Id == type.Id);
            preview.OpeningTypes.Add(type);
            var opening = _opening with
            {
                Id = Guid.NewGuid(), WallId = wall.Id, Offset = span / 2, Width = type.Width, Height = type.Height,
                Sill = sill, OpeningTypeId = type.Id, FlipHand = _flipHand, FlipFacing = _flipFacing
            };
            preview.Openings.Add(opening);
            OpeningTypeRules.Validate(preview);
            WallGeometry.Validate(preview.Walls, preview.Openings);
            var scene = BuildingMesher.Build(preview);
            _message.Text = string.Empty;
            _drawn = (opening.Begin, opening.End, span, wall.Thickness, type);
            Show3D(scene, frame);
            DrawPlan();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            _message.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    private void Show3D(SceneMesh scene, bool frame)
    {
        _viewport.Children.Clear();
        _viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(150, 160, 156)) });
        _viewport.Children.Add(new ModelVisual3D { Content = new DirectionalLight(Color.FromRgb(255, 246, 232), new Vector3D(0.45, -0.70, -0.55)) });
        foreach (var model in ScenePresentation.ToWpfModels(scene))
            _viewport.Children.Add(new ModelVisual3D { Content = model });
        (_min, _max) = scene.Bounds();
        var edges = FeatureEdges.Extract(scene.Positions, scene.Indices, maximumEdges: 20_000);
        if (ScenePresentation.ToWpfEdges(edges, Matrix4x4.Identity, 0.006f, Color.FromRgb(52, 56, 62)) is { } lines)
            _viewport.Children.Add(new ModelVisual3D { Content = lines });
        if (frame)
        {
            var framed = NavigationCamera.Frame(_min, _max, azimuthDegrees: -35, elevationDegrees: 18, distanceScale: 1.1f);
            _camera.Position = framed.Position;
            _camera.YawDegrees = framed.YawDegrees;
            _camera.PitchDegrees = framed.PitchDegrees;
            _camera.FieldOfViewDegrees = framed.FieldOfViewDegrees;
        }
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        if (_surface.ActualHeight <= 0) return;
        var radius = Math.Max(1f, (_max - _min).Length() / 2);
        _viewport.Camera = ScenePresentation.ToWpfCamera(_camera, _surface.ActualWidth / _surface.ActualHeight, radius);
    }

    // ---------------------------------------------------------------- plan

    private void DrawPlan()
    {
        _plan.Children.Clear();
        if (_drawn is not { } drawn || _plan.ActualWidth < 60 || _plan.ActualHeight < 60) return;
        var (begin, end, span, thickness, type) = drawn;
        var reach = Math.Max(type.Width, 0.8) + 0.3;
        var scale = Math.Min((_plan.ActualWidth - 60) / (span + 0.4), (_plan.ActualHeight - 60) / (2 * reach));
        Point At(double u, double v) => new(_plan.ActualWidth / 2 + (u - span / 2) * scale, _plan.ActualHeight / 2 - v * scale);
        var half = thickness / 2;
        var ink = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        var fill = new SolidColorBrush(Color.FromRgb(214, 214, 218));

        void Rectangle(double u0, double u1, double v0, double v1, Brush? interior, double stroke)
        {
            var a = At(u0, v1);
            var b = At(u1, v0);
            var shape = new Rectangle { Width = Math.Max(0, b.X - a.X), Height = Math.Max(0, b.Y - a.Y), Fill = interior, Stroke = ink, StrokeThickness = stroke };
            Canvas.SetLeft(shape, a.X);
            Canvas.SetTop(shape, a.Y);
            _plan.Children.Add(shape);
        }
        Rectangle(0, begin, -half, half, fill, 1.4);
        Rectangle(end, span, -half, half, fill, 1.4);

        if (_opening.Kind == OpeningKind.Door)
        {
            var wall = new Wall(Guid.Empty, Guid.Empty, new Point2(0, 0), new Point2(span, 0), thickness, 3);
            var symbol = DoorPlanSymbol.Build(begin, end, thickness, WallGeometry.LeafDepth(type, wall), type.Operation, _flipHand, _flipFacing);
            foreach (var line in symbol.Thresholds)
                _plan.Children.Add(new Line { X1 = At(line.From.U, line.From.V).X, Y1 = At(line.From.U, line.From.V).Y,
                    X2 = At(line.To.U, line.To.V).X, Y2 = At(line.To.U, line.To.V).Y, Stroke = ink, StrokeThickness = 0.8 });
            foreach (var arc in symbol.Swings)
                _plan.Children.Add(new Polyline { Points = new PointCollection(DoorPlanSymbol.Sample(arc).Select(p => At(p.U, p.V))), Stroke = ink, StrokeThickness = 0.9 });
            foreach (var outline in symbol.Outlines)
                _plan.Children.Add(new Polygon
                {
                    Points = new PointCollection(outline.Points.Select(p => At(p.U, p.V))),
                    Fill = outline.Part == DoorPlanPart.Casing ? Brushes.White : fill, Stroke = ink,
                    StrokeThickness = outline.Part == DoorPlanPart.Leaf ? 1.6 : 1
                });
        }
        else
        {
            Rectangle(begin, end, -half * 0.55, half * 0.55, Brushes.White, 1);
            foreach (var v in new[] { -half / 3, half / 3 })
                _plan.Children.Add(new Line { X1 = At(begin, v).X, Y1 = At(begin, v).Y, X2 = At(end, v).X, Y2 = At(end, v).Y, Stroke = ink, StrokeThickness = 1.5 });
        }

        var label = new TextBlock { Text = $"{type.Width:0.###} m", FontSize = 11, Foreground = Brushes.DarkSlateGray };
        var anchor = At((begin + end) / 2, -half - 0.15);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, anchor.X - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, anchor.Y);
        _plan.Children.Add(label);
        var dimension = new Line { X1 = At(begin, -half - 0.1).X, Y1 = At(begin, -half - 0.1).Y, X2 = At(end, -half - 0.1).X, Y2 = At(end, -half - 0.1).Y, Stroke = Brushes.SteelBlue, StrokeThickness = 1 };
        _plan.Children.Add(dimension);
    }

    // ---------------------------------------------------------------- applying

    private void Apply()
    {
        if (!TryRead(out var type, out var sill))
        {
            _message.Text = Text("OpeningViews.Invalid");
            return;
        }
        try
        {
            using var group = _session.BeginGroup("Transaction.UpdateOpeningType");
            _session.UpdateOpeningType(_type.Id, _type.FamilyName, type.Name, type.Width, type.Height, type.DefaultSill,
                type.MaterialName, type.FrameWidth, type.GlazingLightCount);
            var current = _session.Document.Openings.Single(item => item.Id == _opening.Id);
            if (_opening.Kind == OpeningKind.Window && Math.Abs(current.Sill - sill) > 1e-9)
                _session.UpdateOpening(_opening.Id, current.Offset, current.Width, sill, current.Height);
            if (_opening.Kind == OpeningKind.Door && (current.FlipHand != _flipHand || current.FlipFacing != _flipFacing))
                _session.FlipOpening(_opening.Id, _flipHand, _flipFacing);
            group.Assimilate();
            Changed = true;
            _message.Text = string.Empty;
            DialogResult = true;
        }
        catch (InvalidDataException exception)
        {
            _message.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;
}
