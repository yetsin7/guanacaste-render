using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The panel schedule, as in Revit: for every panel its main breaker and total load, and for every circuit its breaker,
/// devices, load, current, share of the breaker, conductor, run length, voltage drop and state. A breaker can be switched
/// or resized here; the plan and the render follow at once, and an overloaded circuit shows tripped.
/// </summary>
internal sealed class ElectricalPanelWindow : Window
{
    private readonly DocumentSession _session;
    private readonly Action _changed;
    private readonly StackPanel _content = new() { Margin = new Thickness(12) };

    private sealed record Row(Guid Id, int Number, string Name, double Breaker, int Devices, string Load, string Current,
        string Use, string Conductor, string Length, string Drop, string State, Brush StateBrush, string Voltage);

    public ElectricalPanelWindow(DocumentSession session, Action changed)
    {
        _session = session;
        _changed = changed;
        Title = Text("PanelSchedule.Title");
        Width = 1080;
        Height = 620;
        Content = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Activated += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        _content.Children.Clear();
        var analysis = ElectricalAnalysis.Analyze(_session.Document);
        foreach (var panel in analysis.Panels)
        {
            _content.Children.Add(new TextBlock
            {
                Text = string.Format(CultureInfo.CurrentCulture, Text("PanelSchedule.PanelHeader"), panel.Panel.Name,
                    panel.Panel.MainBreakerAmps, panel.LoadWatts / 1000, panel.Current, panel.Utilization * 100, StateText(panel.Status)),
                FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6),
                Foreground = StateBrush(panel.Status)
            });
            if (panel.GenerationWatts > 0)
                _content.Children.Add(new TextBlock
                {
                    Text = string.Format(CultureInfo.CurrentCulture, Text(panel.BackfeedWithinLimit ? "PanelSchedule.SolarOk" : "PanelSchedule.SolarBackfeed"),
                        panel.GenerationWatts / 1000, panel.Panel.MainBreakerAmps * 1.2),
                    Foreground = panel.BackfeedWithinLimit ? Brushes.SeaGreen : Brushes.DarkOrange, Margin = new Thickness(0, 0, 0, 6)
                });
            var grid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 80
            };
            void Column(string header, string path, double width) => grid.Columns.Add(new DataGridTextColumn
            {
                Header = Text(header), Binding = new System.Windows.Data.Binding(path), Width = width
            });
            Column("PanelSchedule.Number", nameof(Row.Number), 40);
            Column("PanelSchedule.Name", nameof(Row.Name), 170);
            Column("PanelSchedule.Breaker", nameof(Row.Breaker), 80);
            Column("PanelSchedule.VoltageColumn", nameof(Row.Voltage), 60);
            Column("PanelSchedule.Devices", nameof(Row.Devices), 80);
            Column("PanelSchedule.Load", nameof(Row.Load), 90);
            Column("PanelSchedule.Current", nameof(Row.Current), 90);
            Column("PanelSchedule.Use", nameof(Row.Use), 70);
            Column("PanelSchedule.Conductor", nameof(Row.Conductor), 120);
            Column("PanelSchedule.Length", nameof(Row.Length), 80);
            Column("PanelSchedule.Drop", nameof(Row.Drop), 80);
            var state = new DataGridTemplateColumn { Header = Text("PanelSchedule.State"), Width = 120 };
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Row.State)));
            text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Row.StateBrush)));
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            state.CellTemplate = new DataTemplate { VisualTree = text };
            grid.Columns.Add(state);
            grid.ItemsSource = panel.Circuits.Select(report => new Row(report.Circuit.Id, report.Circuit.Number, report.Circuit.Name,
                report.Circuit.BreakerAmps, report.Devices.Count, $"{report.LoadWatts:0} W", $"{report.Current:0.0} A",
                $"{report.Utilization * 100:0} %", $"{report.ConductorAwg} AWG ({report.ConductorSquareMillimeters:0.##} mm²)",
                $"{report.LengthMeters:0.0} m", $"{report.VoltageDropPercent:0.0} %" + (report.VoltageDropPercent > 3 ? " ⚠" : string.Empty),
                StateText(report.Status), StateBrush(report.Status), $"{report.Circuit.Voltage:0} V")).ToList();
            _content.Children.Add(grid);

            var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
            Button Add(string key, Action<Row?> action)
            {
                var button = new Button { Content = Text(key), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
                button.Click += (_, _) => Run(() => action(grid.SelectedItem as Row));
                buttons.Children.Add(button);
                return button;
            }
            var panelId = panel.Panel.Id;
            Add("PanelSchedule.NewCircuit", _ =>
                _session.AddCircuit(panelId, string.Format(CultureInfo.CurrentCulture, Text("Electrical.CircuitName"),
                    _session.Document.ElectricalCircuits.Count(circuit => circuit.PanelId == panelId) + 1), 20));
            Add("PanelSchedule.ToggleBreaker", row =>
            {
                if (row is null) return;
                var circuit = _session.Document.ElectricalCircuits.Single(candidate => candidate.Id == row.Id);
                _session.SetBreaker(row.Id, circuit.BreakerAmps, !circuit.BreakerOn);
            });
            Add("PanelSchedule.Larger", row => Resize(row, +1));
            Add("PanelSchedule.Smaller", row => Resize(row, -1));
            Add("PanelSchedule.Rename", row =>
            {
                if (row is null) return;
                var name = PromptWindow.Ask(this, Text("PanelSchedule.Rename"), row.Name);
                if (!string.IsNullOrWhiteSpace(name)) _session.RenameCircuit(row.Id, name);
            });
            Add("PanelSchedule.Remove", row =>
            {
                if (row is not null) _session.RemoveCircuit(row.Id);
            });
            Add("PanelSchedule.Voltage", row =>
            {
                if (row is null) return;
                var circuit = _session.Document.ElectricalCircuits.Single(candidate => candidate.Id == row.Id);
                _session.SetCircuitVoltage(row.Id, circuit.Voltage >= 200 ? 120 : 240);
            });
            Add("PanelSchedule.MainBreaker", _ => _session.ToggleSwitch(panelId));
            _content.Children.Add(buttons);
        }
        _content.Children.Add(new TextBlock
        {
            Text = Text("PanelSchedule.Notes"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0)
        });
    }

    private void Resize(Row? row, int step)
    {
        if (row is null) return;
        var ratings = ElectricalStandards.BreakerRatings;
        var circuit = _session.Document.ElectricalCircuits.Single(candidate => candidate.Id == row.Id);
        var index = Math.Clamp(ratings.ToList().IndexOf(circuit.BreakerAmps) + step, 0, ratings.Count - 1);
        _session.SetBreaker(row.Id, ratings[index], circuit.BreakerOn);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _changed();
        Refresh();
    }

    private static string StateText(CircuitStatus status) => Text($"CircuitStatus.{status}");

    private static Brush StateBrush(CircuitStatus status) => status switch
    {
        CircuitStatus.Tripped or CircuitStatus.WrongVoltage => Brushes.Red,
        CircuitStatus.Warning => Brushes.DarkOrange,
        CircuitStatus.Off => Brushes.Gray,
        CircuitStatus.Ok => Brushes.SeaGreen,
        _ => Brushes.SlateGray
    };

    private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;
}
