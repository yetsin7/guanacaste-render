using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _profileWindow;
    private Guid _profileWallId;

    /// <summary>Routes the Edit Profile and Reset Profile buttons of the Wall tab.</summary>
    private bool TryExecuteWallProfileCommand(string commandId)
    {
        switch (commandId)
        {
            case "EditProfile": ShowWallProfileEditor(); return true;
            case "ResetProfile": ResetSelectedWallProfiles(); return true;
            default: return false;
        }
    }

    private Wall? SelectedWallForProfile() =>
        SelectedIds().Select(id => _session.Document.Walls.FirstOrDefault(wall => wall.Id == id)).FirstOrDefault(wall => wall is not null);

    private void ResetSelectedWallProfiles()
    {
        var ids = SelectedIds();
        if (ids.Count == 0 || !_session.Document.Walls.Any(wall => ids.Contains(wall.Id)))
        {
            StatusText.Text = GetText("Status.WallProfileNone");
            return;
        }
        try
        {
            var changed = _session.ResetWallProfiles(ids);
            StatusText.Text = changed == 0 ? GetText("Status.WallProfileNothingToReset") : FormatText("Status.WallProfileReset", changed);
            if (changed > 0) ApplyViewVisibility();
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    /// <summary>Opens the profile editor for the selected wall: its top as a list of points, a presets row and a live preview.</summary>
    private void ShowWallProfileEditor()
    {
        if (SelectedWallForProfile() is not { } wall)
        {
            StatusText.Text = GetText("Status.WallProfileNone");
            return;
        }
        _profileWindow?.Close();
        _profileWallId = wall.Id;
        var window = new Window
        {
            Title = GetText("WallProfile.Title") + " · " + wall.Id.ToString("N")[..6], Owner = this, Width = 460,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.Closed += (_, _) => _profileWindow = null;
        _profileWindow = window;

        var text = new TextBox
        {
            Name = "ProfileText", AcceptsReturn = false, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(4),
            Text = WallProfiles.ToText(wall.TopProfile ?? [new Point2(0, wall.Height), new Point2(wall.Length, wall.Height)])
        };
        var message = new TextBlock { Name = "ProfileMessage", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MinHeight = 18 };
        var preview = new Canvas { Name = "ProfilePreview", Width = 420, Height = 130, Background = Brushes.White, Margin = new Thickness(0, 0, 0, 6) };

        void Redraw()
        {
            preview.Children.Clear();
            var current = _session.Document.Walls.FirstOrDefault(item => item.Id == _profileWallId);
            if (current is null) return;
            var points = WallProfiles.TryParse(text.Text);
            message.Text = points is null ? GetText("WallProfile.NotValid") : string.Empty;
            var scale = Math.Min(400 / Math.Max(current.Length, 0.1), 110 / Math.Max(current.Height, 0.1));
            double X(double along) => 10 + along * scale;
            double Y(double height) => 120 - height * scale;
            var frame = new Rectangle { Width = current.Length * scale, Height = current.Height * scale, Stroke = Brushes.Gray, StrokeDashArray = [3, 3] };
            Canvas.SetLeft(frame, X(0));
            Canvas.SetTop(frame, Y(current.Height));
            preview.Children.Add(frame);
            if (points is null) return;
            var outline = new PointCollection { new(X(0), Y(0)), new(X(current.Length), Y(0)) };
            foreach (var station in points.Select(point => point.X).Where(x => x > 0 && x < current.Length).Append(current.Length).Append(0).Order().Reverse())
                outline.Add(new Point(X(station), Y(Math.Min(current.Height, WallProfiles.HeightAt(points, station)))));
            preview.Children.Add(new Polygon { Points = outline, Fill = new SolidColorBrush(Color.FromArgb(90, 120, 150, 120)), Stroke = Brushes.DarkSlateGray, StrokeThickness = 1.5 });
        }
        text.TextChanged += (_, _) => Redraw();

        Button Make(string name, string label, RoutedEventHandler click)
        {
            var button = new Button { Name = name, Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 2, 8, 2) };
            button.Click += click;
            return button;
        }
        void Preset(IReadOnlyList<Point2> points) => text.Text = WallProfiles.ToText(points);

        var presets = new WrapPanel();
        presets.Children.Add(Make("ProfileGable", GetText("WallProfile.Gable"), (_, _) =>
        {
            var current = _session.Document.Walls.First(item => item.Id == _profileWallId);
            Preset(WallProfiles.Gable(current.Length, current.Height));
        }));
        presets.Children.Add(Make("ProfileShed", GetText("WallProfile.Shed"), (_, _) =>
        {
            var current = _session.Document.Walls.First(item => item.Id == _profileWallId);
            Preset(WallProfiles.Shed(current.Length, current.Height));
        }));
        presets.Children.Add(Make("ProfileStepped", GetText("WallProfile.Stepped"), (_, _) =>
        {
            var current = _session.Document.Walls.First(item => item.Id == _profileWallId);
            Preset(WallProfiles.Stepped(current.Length, current.Height));
        }));

        var actions = new WrapPanel();
        actions.Children.Add(Make("ProfileApply", GetText("WallProfile.Apply"), (_, _) =>
        {
            var points = WallProfiles.TryParse(text.Text);
            if (points is null)
            {
                StatusText.Text = GetText("WallProfile.NotValid");
                return;
            }
            try
            {
                _session.SetWallProfile(_profileWallId, points);
                StatusText.Text = GetText("Status.WallProfileApplied");
                ApplyViewVisibility();
            }
            catch (InvalidDataException exception)
            {
                StatusText.Text = LocalizedErrors.Translate(exception.Message);
                message.Text = StatusText.Text;
            }
        }));
        actions.Children.Add(Make("ProfileReset", GetText("WallProfile.Reset"), (_, _) =>
        {
            try
            {
                var changed = _session.ResetWallProfiles([_profileWallId]);
                StatusText.Text = changed == 0 ? GetText("Status.WallProfileNothingToReset") : FormatText("Status.WallProfileReset", changed);
                var current = _session.Document.Walls.First(item => item.Id == _profileWallId);
                text.Text = WallProfiles.ToText([new Point2(0, current.Height), new Point2(current.Length, current.Height)]);
                if (changed > 0) ApplyViewVisibility();
            }
            catch (InvalidDataException exception)
            {
                StatusText.Text = LocalizedErrors.Translate(exception.Message);
            }
        }));
        actions.Children.Add(Make("ProfileClose", GetText("Phases.Close"), (_, _) => window.Close()));

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = FormatText("WallProfile.Hint", wall.Length, wall.Height), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(presets);
        layout.Children.Add(text);
        layout.Children.Add(preview);
        layout.Children.Add(message);
        layout.Children.Add(actions);
        window.Content = layout;
        Redraw();
        window.Show();
    }

    /// <summary>The wall's face in elevation: a polygon that follows its profile, in the canvas of an elevation or section.</summary>
    private static Polygon ProfiledWallShape(Wall wall, Func<double, double> horizontal, Func<double, double> vertical)
    {
        var profile = wall.TopProfile!;
        var points = new PointCollection { new(horizontal(0), vertical(0)), new(horizontal(wall.Length), vertical(0)) };
        foreach (var station in profile.Select(point => point.X).Where(x => x > 0 && x < wall.Length).Append(wall.Length).Append(0).Order().Reverse())
            points.Add(new Point(horizontal(station), vertical(wall.TopHeightAt(station))));
        return new Polygon { Points = points };
    }
}
