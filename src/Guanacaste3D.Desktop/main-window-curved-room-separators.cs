using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Waiting for one click selecting the center of a non-solid circular/arc room separator.</summary>
    private (double Radius, double Start, double Sweep)? _curvedRoomSeparatorDraft;

    private void BeginCurvedRoomSeparatorTool()
    {
        SetWallTool(false);
        CancelStructuralTool();
        var options = RequestCurvedRoomSeparatorParameters(null);
        if (options is null) return;
        _curvedRoomSeparatorDraft = options;
        ViewTabs.SelectedIndex = 0;
        StatusText.Text = GetText("Status.CurvedRoomSeparatorPickCenter");
        RenderPlan();
    }

    private (double Radius, double Start, double Sweep)? RequestCurvedRoomSeparatorParameters(
        CurvedRoomSeparator? current)
    {
        var defaultText = current is null ? "2;0;360"
            : string.Join(";", new[] { current.Radius, current.StartAngleDegrees, current.SweepAngleDegrees }
                .Select(number => number.ToString("0.###", CultureInfo.InvariantCulture)));
        var answer = PromptWindow.Ask(this, GetText("Dialog.CurvedRoomSeparatorParameters"), defaultText);
        if (answer is null) return null;
        var fields = answer.Split(';', StringSplitOptions.TrimEntries);
        bool Number(string input, out double result) =>
            double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
            double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        if (fields.Length != 3 || !Number(fields[0], out var radius) ||
            !Number(fields[1], out var start) || !Number(fields[2], out var sweep) ||
            !double.IsFinite(radius) || radius is < 0.1 or > 10000 ||
            !double.IsFinite(start) || !double.IsFinite(sweep) ||
            Math.Abs(sweep) is < 1 or > 360)
        {
            StatusText.Text = GetText("Status.CurvedRoomSeparatorInvalid");
            return null;
        }
        return (radius, start, sweep);
    }

    /// <summary>Places the curved room boundary as one undoable document transaction.</summary>
    private bool TryHandleCurvedRoomSeparatorClick(MouseButtonEventArgs e)
    {
        if (_curvedRoomSeparatorDraft is not { } draft) return false;
        var center = CanvasToWorld(PlanPosition(e));
        try
        {
            _session.AddCurvedRoomSeparator(_activeLevelId, center,
                draft.Radius, draft.Start, draft.Sweep);
            StatusText.Text = GetText("Status.CurvedRoomSeparatorCreated");
            _curvedRoomSeparatorDraft = null;
            RefreshAll();
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
        return true;
    }

    private bool CancelCurvedRoomSeparatorTool()
    {
        if (_curvedRoomSeparatorDraft is null) return false;
        _curvedRoomSeparatorDraft = null;
        StatusText.Text = GetText("Status.SelectionMode");
        RenderPlan();
        return true;
    }

    /// <summary>
    /// Draws editable arc separators in plans only; they never appear as solids in 3D or render.
    /// Use the context menu on a curve to edit its radius or remove it without touching any wall.
    /// </summary>
    private void RenderCurvedRoomSeparators()
    {
        foreach (var arc in _session.Document.CurvedRoomSeparators.Where(arc => arc.LevelId == _activeLevelId))
        {
            var points = arc.SamplePoints();
            var menu = new ContextMenu();
            var edit = new MenuItem { Header = GetText("Arch.EditCurvedRoomSeparator") };
            edit.Click += (_, _) =>
            {
                var values = RequestCurvedRoomSeparatorParameters(arc);
                if (values is not { } next) return;
                try
                {
                    _session.UpdateCurvedRoomSeparator(arc.Id, arc.Center,
                        next.Radius, next.Start, next.Sweep);
                    RefreshAll();
                }
                catch (InvalidDataException ex) { StatusText.Text = LocalizedErrors.Translate(ex.Message); }
            };
            var remove = new MenuItem { Header = GetText("Arch.DeleteCurvedRoomSeparator") };
            remove.Click += (_, _) =>
            {
                _session.RemoveCurvedRoomSeparator(arc.Id);
                RefreshAll();
            };
            menu.Items.Add(edit);
            menu.Items.Add(remove);
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var start = WorldToCanvas(points[i]);
                var end = WorldToCanvas(points[i + 1]);
                var line = new Line
                {
                    X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
                    Stroke = Brushes.MediumPurple, StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 3 },
                    Tag = arc.Id, ContextMenu = menu
                };
                PlanCanvas.Children.Add(line);
            }
        }
    }
}
