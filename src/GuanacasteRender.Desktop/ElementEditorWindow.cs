using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The property editor for elements that are not walls, doors or windows: floors, ceilings and roofs (thickness,
/// elevation, finish, roof shape and pitch), columns (section, height, outline), and structural or architectural
/// elements (section, offset, geometry variant, curtain wall grid). Applying edits is a session transaction, so
/// invalid values are rejected without changing the project and undo restores the previous state.
/// </summary>
internal sealed class ElementEditorWindow : Window
{
    private readonly StackPanel _fields = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private Action _apply = () => { };

    private ElementEditorWindow(string title)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        MinHeight = 180;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 12;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");

        var apply = new Button { Content = Text("Parameters.Apply"), IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = Text("Parameters.Close"), IsCancel = true, MinWidth = 90 };
        apply.Click += (_, _) => TryApply();
        close.Click += (_, _) => DialogResult = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(apply);
        buttons.Children.Add(close);
        var layout = new StackPanel { Margin = new Thickness(16) };
        layout.Children.Add(_fields);
        layout.Children.Add(_status);
        layout.Children.Add(buttons);
        Content = layout;
    }

    private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;

    /// <summary>Runs the edit; on success closes with a positive result, otherwise explains what was rejected.</summary>
    private void TryApply()
    {
        try
        {
            _apply();
            DialogResult = true;
        }
        catch (FormatException)
        {
            _status.Text = Text("Editor.InvalidNumber");
        }
        catch (InvalidDataException exception)
        {
            _status.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    private TextBox AddText(string labelKey, string value, bool readOnly = false)
    {
        var label = new TextBlock { Text = Text(labelKey), Margin = new Thickness(0, 6, 0, 2) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        var box = new TextBox { Text = value, IsReadOnly = readOnly };
        _fields.Children.Add(label);
        _fields.Children.Add(box);
        return box;
    }

    private TextBox AddNumber(string labelKey, double value) =>
        AddText(labelKey, value.ToString("0.###", CultureInfo.CurrentCulture));

    private ComboBox AddChoice<T>(string labelKey, IEnumerable<T> values, T current, Func<T, string> name) where T : notnull
    {
        var label = new TextBlock { Text = Text(labelKey), Margin = new Thickness(0, 6, 0, 2) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        var box = new ComboBox();
        foreach (var value in values)
        {
            var item = new ComboBoxItem { Content = name(value), Tag = value };
            box.Items.Add(item);
            if (EqualityComparer<T>.Default.Equals(value, current)) box.SelectedItem = item;
        }
        _fields.Children.Add(label);
        _fields.Children.Add(box);
        return box;
    }

    /// <summary>Parses a length typed in the current or invariant culture.</summary>
    private static double Number(TextBox box)
    {
        var text = box.Text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current)) return current;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)) return invariant;
        throw new FormatException();
    }

    private static T Chosen<T>(ComboBox box) => (T)((ComboBoxItem)box.SelectedItem).Tag;

    /// <summary>The editor for a floor, ceiling or roof.</summary>
    public static ElementEditorWindow ForFloor(DocumentSession session, Guid floorId)
    {
        var floor = session.Document.Floors.Single(candidate => candidate.Id == floorId);
        var window = new ElementEditorWindow(Text("Editor.FloorTitle"));
        var name = window.AddText("Parameters.Name", floor.Name);
        var thickness = window.AddNumber("Editor.Thickness", floor.Thickness);
        var offset = window.AddNumber("Editor.ElevationOffset", floor.ElevationOffset);
        var finish = window.AddChoice("Editor.Finish", Enum.GetValues<FinishKind>(), floor.Finish, kind => Text($"Finish.{kind}"));
        ComboBox? shape = null;
        TextBox? pitch = null;
        TextBox? overhang = null;
        ComboBox? gables = null;
        if (floor.Kind == FloorKind.Roof)
        {
            shape = window.AddChoice("Editor.RoofShape", Enum.GetValues<RoofShape>(), floor.Shape, value => Text($"RoofShape.{value}"));
            pitch = window.AddNumber("Editor.Pitch", floor.Pitch);
            overhang = window.AddNumber("Editor.Overhang", floor.Overhang);
            gables = window.AddChoice("Editor.GableWalls", new[] { true, false }, floor.GableWalls,
                value => Text(value ? "Editor.Yes" : "Editor.No"));
        }
        window._apply = () =>
        {
            session.UpdateFloor(floorId, name.Text, Number(thickness), Number(offset));
            if (Chosen<FinishKind>(finish) != floor.Finish) session.SetFloorFinish(floorId, Chosen<FinishKind>(finish));
            if (shape is not null && pitch is not null)
            {
                var chosenShape = Chosen<RoofShape>(shape);
                var chosenPitch = Number(pitch);
                if (chosenShape != floor.Shape || Math.Abs(chosenPitch - floor.Pitch) > 1e-9)
                    session.SetRoofShape(floorId, chosenShape, chosenPitch);
            }
            if (overhang is not null && gables is not null)
            {
                var chosenOverhang = Number(overhang);
                var chosenGables = Chosen<bool>(gables);
                if (Math.Abs(chosenOverhang - floor.Overhang) > 1e-9 || chosenGables != floor.GableWalls)
                    session.SetRoofEdges(floorId, chosenOverhang, chosenGables);
            }
        };
        return window;
    }

    /// <summary>The editor for a column.</summary>
    public static ElementEditorWindow ForColumn(DocumentSession session, Guid columnId)
    {
        var column = session.Document.Columns.Single(candidate => candidate.Id == columnId);
        var window = new ElementEditorWindow(Text("Editor.ColumnTitle"));
        var shape = window.AddChoice("Editor.ColumnShape", Enum.GetValues<ColumnShape>(), column.Shape, value => Text($"ColumnShape.{value}"));
        var width = window.AddNumber("Editor.Width", column.Width);
        var depth = window.AddNumber("Editor.Depth", column.Depth);
        var height = window.AddNumber("Editor.Height", column.Height);
        window._apply = () => session.UpdateColumn(columnId, Number(width), Number(depth), Number(height), Chosen<ColumnShape>(shape));
        return window;
    }

    /// <summary>
    /// Shows the comfort warnings the typed riser and tread would trigger, without touching the project: the stair is
    /// rebuilt on a copy, so the designer can see "too high" or "too shallow" before applying.
    /// </summary>
    private void PreviewStair(StructuralElement stair, TextBox? width, TextBox? height, TextBox riser, TextBox tread, ComboBox? style)
    {
        try
        {
            var trial = stair with
            {
                Width = width is null ? stair.Width : Number(width), Height = Number(height!),
                Style = style is null ? stair.Style : Chosen<ElementStyle>(style),
                RiserHeight = Number(riser), TreadDepth = Number(tread)
            };
            if (trial.Height <= 0 || trial.RiserHeight <= 0 || trial.TreadDepth <= 0) { _status.Text = ""; return; }
            if (trial.Style != ElementStyle.Spiral && trial.Length > 1e-9)
            {
                var scale = StairGeometry.RunLengthForTread(trial, trial.TreadDepth) / trial.Length;
                trial = trial with { End = new Point2(trial.Start.X + (trial.End.X - trial.Start.X) * scale,
                    trial.Start.Y + (trial.End.Y - trial.Start.Y) * scale) };
            }
            var lines = StairGeometry.Check(trial).Select(warning => string.Format(CultureInfo.CurrentCulture,
                Text("Stair.Warning." + warning.Kind), warning.Value * 100, warning.Limit * 100)).ToList();
            _status.Text = lines.Count == 0 ? string.Format(CultureInfo.CurrentCulture, Text("Stair.Warning.Ok"),
                trial.Height / StairGeometry.RiserCount(trial) * 100, StairGeometry.ActualTread(trial) * 100)
                : "⚠ " + string.Join(Environment.NewLine + "⚠ ", lines);
        }
        catch (FormatException) { _status.Text = ""; }
    }

    /// <summary>The editor for a structural or architectural element; only the fields that mean something for its kind show.</summary>
    public static ElementEditorWindow ForStructural(DocumentSession session, Guid elementId, bool gridFocus)
    {
        var element = session.Document.StructuralElements.Single(candidate => candidate.Id == elementId);
        var window = new ElementEditorWindow(Text("Editor.ElementTitle"));
        var name = window.AddText("Parameters.Name", element.Name);
        var sectioned = element.HasSolid && element.Kind is not (StructuralElementKind.Dormer or StructuralElementKind.Skylight);
        var width = sectioned ? window.AddNumber("Editor.Width", element.Width) : null;
        var height = element.HasSolid ? window.AddNumber("Editor.Height", element.Height) : null;
        var offset = element.HasSolid ? window.AddNumber("Editor.Offset", element.Offset) : null;
        if (element.Kind == StructuralElementKind.Area)
            window.AddText("Editor.PlanArea", $"{element.PlanArea:0.00} m²", readOnly: true);
        var styles = ArchitecturalShapes.StylesFor(element.Kind);
        var style = styles.Count > 1
            ? window.AddChoice("Editor.Style", styles, element.Style, value => Text($"ElementStyle.{value}"))
            : null;
        TextBox? gridWidth = null, gridHeight = null;
        if (element.Kind == StructuralElementKind.CurtainWall)
        {
            gridWidth = window.AddNumber("Editor.GridWidth", element.GridWidth > 0 ? element.GridWidth : 1.5);
            gridHeight = window.AddNumber("Editor.GridHeight", element.GridHeight > 0 ? element.GridHeight : 1.2);
            (gridFocus ? gridWidth : width ?? gridWidth).Loaded += (_, _) => (gridFocus ? gridWidth : width ?? gridWidth).Focus();
        }
        TextBox? riser = null, tread = null;
        if (element.Kind == StructuralElementKind.Stair)
        {
            riser = window.AddNumber("Editor.StairRiser", element.Height / StairGeometry.RiserCount(element));
            tread = window.AddNumber("Editor.StairTread", StairGeometry.ActualTread(element));
            void Preview() => window.PreviewStair(element, width, height, riser, tread, style);
            foreach (var box in new[] { riser, tread, height, width }.OfType<TextBox>()) box.TextChanged += (_, _) => Preview();
            style?.SelectionChanged += (_, _) => Preview();
            Preview();
        }
        window._apply = () =>
        {
            if (riser is not null && tread is not null)
            {
                using var group = session.BeginGroup("Transaction.UpdateStructuralElement");
                session.UpdateStructuralElement(elementId, name.Text, width is null ? element.Width : Number(width),
                    Number(height!), Number(offset!), style is null ? element.Style : Chosen<ElementStyle>(style));
                // Keep a dimension automatic unless the user actually changed it from the value shown.
                var shown = session.Document.StructuralElements.Single(candidate => candidate.Id == elementId);
                var wantedRiser = Number(riser);
                var wantedTread = Number(tread);
                var currentRiser = shown.Height / StairGeometry.RiserCount(shown);
                var currentTread = StairGeometry.ActualTread(shown);
                var riserChanged = Math.Abs(wantedRiser - currentRiser) > 5e-4;
                var treadChanged = Math.Abs(wantedTread - currentTread) > 5e-4;
                if (riserChanged || treadChanged)
                    session.SetStairSteps(elementId, riserChanged ? wantedRiser : shown.RiserHeight,
                        treadChanged ? wantedTread : shown.TreadDepth);
                group.Assimilate();
            }
            else if (element.HasSolid)
                session.UpdateStructuralElement(elementId, name.Text, width is null ? element.Width : Number(width),
                    Number(height!), Number(offset!), style is null ? element.Style : Chosen<ElementStyle>(style));
            else session.RenameStructuralElement(elementId, name.Text);
            if (gridWidth is not null && gridHeight is not null)
                session.UpdateCurtainGrid(elementId, Number(gridWidth), Number(gridHeight));
        };
        return window;
    }
}
