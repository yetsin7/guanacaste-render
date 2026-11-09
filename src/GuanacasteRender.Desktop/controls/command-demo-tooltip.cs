using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>The short looping scene that demonstrates how a ribbon command is used.</summary>
public enum CommandDemoKind
{
    /// <summary>No demonstration: the tooltip shows only the title and description.</summary>
    None,
    DrawWall,
    PlaceDoor,
    PlaceWindow,
    PlacePoint,
    DrawLine,
    DrawRectangle,
    MoveElement,
    EditDimension,

    /// <summary>The command switches or creates a view: the plan turns into a 3D drawing.</summary>
    ChangeView,

    /// <summary>The command opens a dialog or panel: the button is clicked and its window appears.</summary>
    OpenPanel
}

/// <summary>
/// Gives every command button a tooltip with its title, a sentence on how it works and a looping animated
/// demonstration, like the one the door tool has. The scene is built only when the tooltip is about to show, so
/// hundreds of buttons cost nothing until the pointer rests on one.
/// </summary>
public static class CommandDemoToolTip
{
    private const double SceneWidth = 248;
    private const double SceneHeight = 132;
    private static readonly Duration Loop = new(TimeSpan.FromSeconds(4));
    private static readonly Uri[] DemoStrings =
    [
        new("/Guanacaste3D.Desktop;component/resources/localization.demos.es.xaml", UriKind.Relative),
        new("/Guanacaste3D.Desktop;component/resources/localization.tooltips.es.xaml", UriKind.Relative)
    ];

    /// <summary>
    /// Chooses the scene for a command id. Specific tools get their own scene; the rest are grouped by what they do
    /// (place a point object, draw a line, draw an area, edit existing elements, measure, change a view) and anything else
    /// is shown as a button that opens a panel, so no command is left without a demonstration.
    /// </summary>
    public static CommandDemoKind KindFor(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return CommandDemoKind.None;
        var id = commandId;
        bool Has(params string[] words) => words.Any(word => id.Contains(word, StringComparison.OrdinalIgnoreCase));
        return id switch
        {
            "Wall" or "WallByFace" or "Wall.Structural" => CommandDemoKind.DrawWall,
            "Door" => CommandDemoKind.PlaceDoor,
            "Window" => CommandDemoKind.PlaceWindow,
            _ when Has("Dimension", "Tag", "Spot", "Annotation", "Text", "Keynote", "Symbol", "Detail") => CommandDemoKind.EditDimension,
            _ when Has("Move", "Rotate", "Mirror", "Align", "Offset", "Array", "Copy", "Split", "Trim", "Extend", "Scale",
                "Delete", "Similar", "Join", "Paste", "Match", "Group", "Edit", "Modify", "Attach", "Detach", "Flip") => CommandDemoKind.MoveElement,
            _ when Has("Plan", "Elevation", "Section", "3D", "Model3D", "Camera", "Walkthrough", "Render", "Viewer", "View", "Sheet",
                "Schedule", "Legend", "Callout", "Perspective", "Orthographic", "Orbit", "Zoom", "Shadows") => CommandDemoKind.ChangeView,
            _ when Has("Floor", "Slab", "Roof", "Ceiling", "Room", "Area", "Space", "Zone", "Shaft", "Region", "Mass", "Site", "Pad",
                "Toposurface", "Subregion", "Footing", "Foundation") => CommandDemoKind.DrawRectangle,
            _ when Has("Line", "Railing", "Beam", "Brace", "Duct", "Pipe", "Conduit", "Cable", "Tray", "Gutter", "Ramp", "Stair",
                "Grid", "Axis", "Truss", "Sweep", "Reveal", "Path", "Curve", "Arc", "Spline", "Circle", "Polyline", "Mullion",
                "Curtain") => CommandDemoKind.DrawLine,
            _ when Has("Column", "Pier", "Component", "Furniture", "Equipment", "Fixture", "Light", "Sprinkler", "Device", "Plant",
                "Parking", "Point", "Fitting", "Terminal", "Opening", "Bolt", "Anchor", "Plate", "Shear", "Rebar", "Mullion") => CommandDemoKind.PlacePoint,
            _ => CommandDemoKind.OpenPanel
        };
    }

    /// <summary>Gives a button its animated tooltip, shown after the pointer rests on it.</summary>
    public static void Attach(FrameworkElement button, string commandId, string title)
    {
        ToolTipService.SetInitialShowDelay(button, 350);
        ToolTipService.SetShowDuration(button, 30000);
        ToolTipService.SetBetweenShowDelay(button, 250);
        var tip = new ToolTip
        {
            Padding = new Thickness(10), Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = true,
            // The title is the content until the scene exists, so UI automation and accessibility never see an empty tip.
            Content = title
        };
        var built = false;
        button.ToolTip = tip;
        button.ToolTipOpening += (_, _) =>
        {
            if (built) return;
            built = true;
            Populate(tip, commandId, title);
        };
    }

    /// <summary>Creates a localized demonstration tooltip for explicit display and UI verification.</summary>
    public static ToolTip CreateDemo(string commandId, string title)
    {
        var tip = new ToolTip
        {
            Padding = new Thickness(10), Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = true
        };
        Populate(tip, commandId, title);
        return tip;
    }

    /// <summary>Fills a tooltip with the title, the explanation and the scene, and ties the scene's animation to it.</summary>
    private static void Populate(ToolTip tip, string commandId, string title)
    {
        EnsureStrings();
        // A fuller name ("Tip.<id>") replaces an ambiguous ribbon label such as "Lineal" or "Por cara".
        title = Text("Tip." + commandId) ?? title;
        var kind = KindFor(commandId);
        var description = Text("Demo." + commandId) ?? Text(DescriptionKey(kind, commandId)) ?? Text("ToolTip." + commandId);
        var layout = new StackPanel { Width = SceneWidth };
        layout.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 14 });
        if (description is not null)
            layout.Children.Add(new TextBlock
            {
                Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(72, 78, 86))
            });
        Scene? scene = null;
        if (kind != CommandDemoKind.None)
        {
            scene = BuildScene(kind);
            layout.Children.Add(new Border
            {
                Child = scene.Canvas, Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 220, 228)), BorderThickness = new Thickness(1),
                ClipToBounds = true
            });
            layout.Children.Add(new TextBlock
            {
                Text = "▶ " + (Text("Demo.Caption") ?? string.Empty) + " · " + (Text("Demo.Hint") ?? string.Empty),
                FontSize = 10.5, Margin = new Thickness(0, 5, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(110, 118, 128))
            });
        }
        tip.Content = layout;
        if (scene is not null)
        {
            tip.Opened += (_, _) => scene.Start();
            tip.Closed += (_, _) => scene.Stop();
        }
    }

    /// <summary>Maps a scene to its explanation; line, point and rectangle tools share one text each.</summary>
    private static string DescriptionKey(CommandDemoKind kind, string commandId) => kind switch
    {
        CommandDemoKind.DrawWall => "Demo.Wall",
        CommandDemoKind.PlaceDoor => "Demo.Door",
        CommandDemoKind.PlaceWindow => "Demo.Window",
        CommandDemoKind.PlacePoint => "Demo.Column",
        CommandDemoKind.DrawLine => "Demo.Line",
        CommandDemoKind.DrawRectangle => "Demo.Rectangle",
        CommandDemoKind.MoveElement => "Demo.Move",
        CommandDemoKind.EditDimension => "Demo.Dimension",
        CommandDemoKind.ChangeView => "Demo.View",
        CommandDemoKind.OpenPanel => "Demo.Panel",
        _ => "Demo." + commandId
    };

    /// <summary>Loads the demonstration strings once, beside the dictionaries the application merged.</summary>
    private static void EnsureStrings()
    {
        if (Application.Current is not { } application) return;
        foreach (var source in DemoStrings)
        {
            if (application.Resources.MergedDictionaries.Any(dictionary => dictionary.Source == source)) continue;
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
        }
    }

    /// <summary>Returns a localized string, or null when the key does not exist.</summary>
    private static string? Text(string key) => Application.Current?.TryFindResource(key) as string;

    /// <summary>Creates the canvas and keyframes for one demonstration.</summary>
    private static Scene BuildScene(CommandDemoKind kind)
    {
        var scene = new Scene();
        scene.DrawGrid();
        switch (kind)
        {
            case CommandDemoKind.DrawWall:
                var first = scene.Wall(30, 100, 30, 100);
                scene.Animate(first, Line.X2Property, (0.3, 30), (1.5, 160), (4, 160));
                var second = scene.Wall(160, 100, 160, 100);
                scene.Animate(second, Line.Y2Property, (1.7, 100), (2.8, 30), (4, 30));
                scene.Label("4.30 m", 80, 108, (0.5, 0), (0.8, 1), (1.5, 1), (1.7, 0));
                scene.Cursor((0, 30, 100), (0.3, 30, 100), (1.5, 160, 100), (1.7, 160, 100), (2.8, 160, 30), (4, 160, 30));
                break;
            case CommandDemoKind.PlaceDoor:
            case CommandDemoKind.PlaceWindow:
                scene.Wall(14, 92, 234, 92);
                var symbol = kind == CommandDemoKind.PlaceDoor ? scene.DoorSymbol() : scene.WindowSymbol();
                scene.Translate(symbol, (0, 40, 24), (1.6, 108, 92), (4, 108, 92));
                scene.Animate(symbol, UIElement.OpacityProperty, (0, 0.55), (1.6, 0.55), (1.7, 1), (3.6, 1), (4, 0));
                scene.Cursor((0, 40, 24), (1.6, 108, 92), (4, 108, 92));
                scene.Click(1.65, 108, 92);
                break;
            case CommandDemoKind.PlacePoint:
                var column = scene.Box(0, 0, 18, 18);
                scene.Translate(column, (0, 40, 30), (1.5, 120, 60), (4, 120, 60));
                scene.Animate(column, UIElement.OpacityProperty, (0, 0.5), (1.5, 0.5), (1.6, 1), (3.6, 1), (4, 0));
                scene.Cursor((0, 49, 39), (1.5, 129, 69), (4, 129, 69));
                scene.Click(1.55, 129, 69);
                break;
            case CommandDemoKind.DrawLine:
                var line = scene.Wall(40, 80, 40, 80, 3);
                scene.Animate(line, Line.X2Property, (0.4, 40), (2, 200), (4, 200));
                scene.Animate(line, Line.Y2Property, (0.4, 80), (2, 46), (4, 46));
                scene.Cursor((0, 40, 80), (0.4, 40, 80), (2, 200, 46), (4, 200, 46));
                scene.Click(0.4, 40, 80);
                scene.Click(2, 200, 46);
                break;
            case CommandDemoKind.DrawRectangle:
                var outline = scene.Box(50, 30, 1, 1, filled: false);
                scene.Animate(outline, FrameworkElement.WidthProperty, (0.4, 1), (2, 150), (4, 150));
                scene.Animate(outline, FrameworkElement.HeightProperty, (0.4, 1), (2, 72), (4, 72));
                scene.Cursor((0, 50, 30), (0.4, 50, 30), (2, 200, 102), (4, 200, 102));
                scene.Click(0.4, 50, 30);
                scene.Click(2, 200, 102);
                break;
            case CommandDemoKind.MoveElement:
                scene.Box(40, 50, 40, 30, filled: false).Opacity = 0.35;
                var moved = scene.Box(0, 0, 40, 30);
                scene.Translate(moved, (0, 40, 50), (0.5, 40, 50), (2.2, 160, 70), (4, 160, 70));
                scene.Cursor((0, 60, 65), (0.5, 60, 65), (2.2, 180, 85), (4, 180, 85));
                break;
            case CommandDemoKind.EditDimension:
                scene.Wall(20, 30, 20, 110);
                var door = scene.Box(0, 0, 10, 26);
                scene.Translate(door, (0, 80, 57), (2, 80, 57), (2.8, 140, 57), (4, 140, 57));
                scene.Label("3.00 m", 40, 36, (0, 1), (2, 1), (2.1, 0), (4, 0));
                scene.Label("5.00 m", 70, 36, (0, 0), (2, 0), (2.1, 1), (4, 1));
                scene.Cursor((0, 200, 110), (1, 58, 42), (2.6, 58, 42), (4, 200, 110));
                scene.Click(1.1, 58, 42);
                break;
            case CommandDemoKind.ChangeView:
            {
                // A plan outline is clicked and turns into an isometric box, the way a command switches the view.
                var plan = scene.Box(70, 40, 108, 56, filled: false);
                scene.Animate(plan, UIElement.OpacityProperty, (0, 1), (1.6, 1), (2, 0), (3.6, 0), (4, 1));
                var solid = scene.Isometric(124, 70);
                scene.Animate(solid, UIElement.OpacityProperty, (0, 0), (1.6, 0), (2, 1), (3.6, 1), (4, 0));
                scene.Cursor((0, 200, 100), (1.2, 22, 14), (1.6, 22, 14), (4, 200, 100));
                scene.Click(1.5, 22, 14);
                break;
            }
            default:
            {
                // A button is clicked and its dialog opens: title bar, two fields and a confirm button.
                scene.Box(10, 8, 28, 16, filled: true);
                var dialog = scene.Dialog(66, 22);
                scene.Animate(dialog, UIElement.OpacityProperty, (0, 0), (1.6, 0), (2, 1), (3.6, 1), (4, 0));
                scene.Translate(dialog, (0, 0, 6), (1.6, 0, 6), (2, 0, 0), (4, 0, 0));
                scene.Cursor((0, 190, 110), (1.2, 22, 14), (1.6, 22, 14), (2.6, 170, 100), (4, 190, 110));
                scene.Click(1.5, 22, 14);
                break;
            }
        }
        return scene;
    }

    /// <summary>A demonstration canvas plus the animations that start and stop with its tooltip.</summary>
    private sealed class Scene
    {
        private static readonly Brush WallBrush = Frozen(Color.FromRgb(74, 84, 94));
        private static readonly Brush AccentBrush = Frozen(Color.FromRgb(0, 113, 227));
        private readonly List<(IAnimatable Target, DependencyProperty Property, AnimationTimeline Animation)> _animations = [];

        public Canvas Canvas { get; } = new()
        {
            Width = SceneWidth, Height = SceneHeight, ClipToBounds = true,
            Background = Frozen(Color.FromRgb(250, 251, 253))
        };

        /// <summary>Starts every animation together so the scene stays in sync.</summary>
        public void Start()
        {
            foreach (var (target, property, animation) in _animations) target.BeginAnimation(property, animation);
        }

        /// <summary>Removes the animations so a closed tooltip uses no rendering time.</summary>
        public void Stop()
        {
            foreach (var (target, property, _) in _animations) target.BeginAnimation(property, null);
        }

        /// <summary>Draws a light drafting grid.</summary>
        public void DrawGrid()
        {
            var brush = Frozen(Color.FromRgb(232, 236, 241));
            for (var x = 0.0; x <= SceneWidth; x += 20)
                Canvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = SceneHeight, Stroke = brush, StrokeThickness = 1 });
            for (var y = 0.0; y <= SceneHeight; y += 20)
                Canvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = SceneWidth, Y2 = y, Stroke = brush, StrokeThickness = 1 });
        }

        /// <summary>Adds a thick wall line with square caps so consecutive walls close their corner.</summary>
        public Line Wall(double x1, double y1, double x2, double y2, double thickness = 8)
        {
            var line = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = WallBrush, StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Square, StrokeEndLineCap = PenLineCap.Square
            };
            Canvas.Children.Add(line);
            return line;
        }

        /// <summary>Adds a filled element footprint or an outline.</summary>
        public Rectangle Box(double x, double y, double width, double height, bool filled = true)
        {
            var box = new Rectangle
            {
                Width = width, Height = height, StrokeThickness = 1.5, Stroke = filled ? WallBrush : AccentBrush,
                Fill = filled ? Frozen(Color.FromRgb(150, 160, 172)) : null,
                StrokeDashArray = filled ? null : new DoubleCollection { 4, 2 }
            };
            Canvas.SetLeft(box, x);
            Canvas.SetTop(box, y);
            Canvas.Children.Add(box);
            return box;
        }

        /// <summary>Draws a door plan symbol whose origin is the centre of its opening on the wall axis.</summary>
        public Canvas DoorSymbol()
        {
            var symbol = new Canvas();
            symbol.Children.Add(new Rectangle { Width = 40, Height = 10, Fill = Brushes.White, Margin = new Thickness(-20, -5, 0, 0) });
            symbol.Children.Add(new Rectangle { Width = 3, Height = 34, Fill = Frozen(Color.FromRgb(196, 160, 112)),
                Stroke = Brushes.SaddleBrown, StrokeThickness = 1, Margin = new Thickness(-18, -36, 0, 0) });
            symbol.Children.Add(new Path
            {
                Data = Geometry.Parse("M -16,-36 A 34,34 0 0 1 18,-2"), Stroke = Brushes.SaddleBrown, StrokeThickness = 1
            });
            Canvas.Children.Add(symbol);
            return symbol;
        }

        /// <summary>Draws a window plan symbol whose origin is the centre of its opening on the wall axis.</summary>
        public Canvas WindowSymbol()
        {
            var symbol = new Canvas();
            symbol.Children.Add(new Rectangle { Width = 48, Height = 10, Fill = Brushes.White, Stroke = Brushes.SteelBlue,
                StrokeThickness = 1, Margin = new Thickness(-24, -5, 0, 0) });
            symbol.Children.Add(new Line { X1 = -24, Y1 = -1.5, X2 = 24, Y2 = -1.5, Stroke = Brushes.SteelBlue, StrokeThickness = 1 });
            symbol.Children.Add(new Line { X1 = -24, Y1 = 1.5, X2 = 24, Y2 = 1.5, Stroke = Brushes.SteelBlue, StrokeThickness = 1 });
            Canvas.Children.Add(symbol);
            return symbol;
        }

        /// <summary>Draws a small isometric box whose origin is the centre of its top face.</summary>
        public Canvas Isometric(double x, double y)
        {
            var box = new Canvas();
            void Face(Color color, params Point[] points) => box.Children.Add(new Polygon
            {
                Points = new PointCollection(points), Fill = Frozen(color), Stroke = WallBrush, StrokeThickness = 1.2,
                StrokeLineJoin = PenLineJoin.Round
            });
            Face(Color.FromRgb(226, 230, 236), new(0, -30), new(46, -8), new(0, 14), new(-46, -8));
            Face(Color.FromRgb(176, 184, 194), new(-46, -8), new(0, 14), new(0, 40), new(-46, 18));
            Face(Color.FromRgb(150, 160, 172), new(0, 14), new(46, -8), new(46, 18), new(0, 40));
            Canvas.SetLeft(box, x);
            Canvas.SetTop(box, y);
            Canvas.Children.Add(box);
            return box;
        }

        /// <summary>Draws a miniature dialog window with a title bar, two fields and a confirm button.</summary>
        public Canvas Dialog(double x, double y)
        {
            var window = new Canvas();
            void Part(double left, double top, double width, double height, Brush fill, Brush? stroke = null)
            {
                var part = new Rectangle { Width = width, Height = height, Fill = fill, Stroke = stroke, StrokeThickness = stroke is null ? 0 : 1 };
                Canvas.SetLeft(part, left);
                Canvas.SetTop(part, top);
                window.Children.Add(part);
            }
            Part(0, 0, 118, 90, Brushes.White, Frozen(Color.FromRgb(168, 176, 188)));
            Part(0, 0, 118, 14, AccentBrush);
            Part(10, 26, 98, 10, Frozen(Color.FromRgb(228, 232, 238)));
            Part(10, 44, 98, 10, Frozen(Color.FromRgb(228, 232, 238)));
            Part(68, 68, 40, 14, AccentBrush);
            Canvas.SetLeft(window, x);
            Canvas.SetTop(window, y);
            Canvas.Children.Add(window);
            return window;
        }

        /// <summary>Adds a dimension value that fades in and out on the given keyframes.</summary>
        public void Label(string text, double x, double y, params (double Time, double Opacity)[] keys)
        {
            var label = new Border
            {
                Background = Brushes.White, BorderBrush = AccentBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 0),
                Child = new TextBlock { Text = text, FontSize = 10.5, Foreground = AccentBrush }
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            Canvas.Children.Add(label);
            Animate(label, UIElement.OpacityProperty, keys);
        }

        /// <summary>Adds the pointer and moves its tip through the given positions.</summary>
        public void Cursor(params (double Time, double X, double Y)[] keys)
        {
            var pointer = new Path
            {
                Data = Geometry.Parse("M0,0 L0,15 L4,11 L7,18 L9.5,17 L6.5,10 L12,10 Z"),
                Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1
            };
            Canvas.Children.Add(pointer);
            Translate(pointer, keys);
        }

        /// <summary>Adds a ripple that marks a click at the given time and point.</summary>
        public void Click(double time, double x, double y)
        {
            var ripple = new Ellipse { Width = 22, Height = 22, Stroke = AccentBrush, StrokeThickness = 2, Opacity = 0 };
            Canvas.SetLeft(ripple, x - 11);
            Canvas.SetTop(ripple, y - 11);
            Canvas.Children.Insert(Canvas.Children.Count - 1, ripple);
            Animate(ripple, UIElement.OpacityProperty, (time, 0), (time + 0.05, 0.9), (time + 0.45, 0), (4, 0));
        }

        /// <summary>Moves an element through positions with a translate transform.</summary>
        public void Translate(UIElement element, params (double Time, double X, double Y)[] keys)
        {
            var transform = new TranslateTransform();
            element.RenderTransform = transform;
            Animate(transform, TranslateTransform.XProperty, keys.Select(key => (key.Time, key.X)).ToArray());
            Animate(transform, TranslateTransform.YProperty, keys.Select(key => (key.Time, key.Y)).ToArray());
        }

        /// <summary>Registers a looping linear keyframe animation; times are seconds within the loop.</summary>
        public void Animate(IAnimatable target, DependencyProperty property, params (double Time, double Value)[] keys)
        {
            var animation = new DoubleAnimationUsingKeyFrames { Duration = Loop, RepeatBehavior = RepeatBehavior.Forever };
            foreach (var (time, value) in keys)
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(time))));
            animation.Freeze();
            _animations.Add((target, property, animation));
            // The first keyframe is also the resting state, so a scene reads correctly before it starts.
            if (target is DependencyObject owner && keys.Length > 0) owner.SetValue(property, keys[0].Value);
        }

        /// <summary>Returns a frozen solid brush.</summary>
        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
