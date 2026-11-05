using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The plumbing schedule: for every system its source, fixture units, design flow, main size, pipe length and state, and
/// for every fixture its branch size and the result of the check — flowing pressure against the minimum the fixture
/// needs, the wait for hot water, or the drain invert and its cover under the floor. Street pressure and outlet depth
/// are edited here and the results follow at once.
/// </summary>
internal sealed class PlumbingScheduleWindow : Window
{
    private readonly DocumentSession _session;
    private readonly Action _changed;
    private readonly Action _connectAll;
    private readonly StackPanel _content = new() { Margin = new Thickness(12) };

    private sealed record Row(string Fixture, string Units, string Branch, string Pressure, string Wait, string Invert,
        string Path, string State, Brush StateBrush, string Note, string Vent);

    public PlumbingScheduleWindow(DocumentSession session, Action changed, Action connectAll)
    {
        _session = session;
        _changed = changed;
        _connectAll = connectAll;
        Title = Text("PlumbingSchedule.Title");
        Width = 1120;
        Height = 680;
        Content = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Activated += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        _content.Children.Clear();
        var document = _session.Document;
        var analysis = PlumbingAnalysis.Analyze(document);

        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        void Tool(string key, Action action)
        {
            var button = new Button { Content = Text(key), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
            button.Click += (_, _) => Run(action);
            tools.Children.Add(button);
        }
        Tool("PlumbingSchedule.ConnectAll", _connectAll);
        foreach (var meter in document.PlumbingPoints.Where(point => point.Kind == PlumbingPointKind.WaterMeter))
        {
            var id = meter.Id;
            Tool("PlumbingSchedule.MorePressure", () => _session.SetSupplyPressure(id, Math.Min(1000, meter.SupplyPressureKPa + 20)));
            Tool("PlumbingSchedule.LessPressure", () => _session.SetSupplyPressure(id, Math.Max(50, meter.SupplyPressureKPa - 20)));
        }
        foreach (var tank in document.PlumbingPoints.Where(point => point.Kind == PlumbingPointKind.SepticTank))
        {
            var id = tank.Id;
            var people = SiteDrainage.Occupants(document, tank);
            Tool("PlumbingSchedule.MorePeople", () => _session.SetSepticOccupants(id, Math.Min(200, people + 1)));
            Tool("PlumbingSchedule.FewerPeople", () => _session.SetSepticOccupants(id, Math.Max(1, people - 1)));
            Tool("PlumbingSchedule.MorePercolation", () => _session.SetPercolation(id, Math.Min(200, tank.Percolation + 10)));
            Tool("PlumbingSchedule.LessPercolation", () => _session.SetPercolation(id, Math.Max(5, tank.Percolation - 10)));
        }
        foreach (var tank in document.PlumbingPoints.Where(point => point.Kind is PlumbingPointKind.Cistern or PlumbingPointKind.ElevatedTank))
        {
            var id = tank.Id;
            var current = analysis.Storage.FirstOrDefault(storage => storage.Id == id)?.CapacityLiters ?? 0;
            Tool("PlumbingSchedule.BiggerTank", () => _session.SetTankCapacity(id, WaterStorage.TankSizes.FirstOrDefault(size => size > current, current)));
            if (tank.Kind == PlumbingPointKind.Cistern)
            {
                var sizes = WaterStorage.PumpSizes;
                var next = sizes[(sizes.ToList().IndexOf(tank.PumpHorsepower) + 1) % sizes.Count];
                Tool("PlumbingSchedule.PumpSize", () => _session.SetPump(id, next, tank.PumpPressureKPa));
            }
            else
            {
                Tool("PlumbingSchedule.HigherTank", () => _session.SetTankHeight(id, Math.Min(40, tank.TankHeight + 1)));
                Tool("PlumbingSchedule.LowerTank", () => _session.SetTankHeight(id, Math.Max(1, tank.TankHeight - 1)));
            }
        }
        foreach (var storm in document.PlumbingPoints.Where(point => point.Kind == PlumbingPointKind.StormOutlet))
        {
            var id = storm.Id;
            Tool("PlumbingSchedule.MoreRain", () => _session.SetRainfall(id, Math.Min(400, storm.RainfallMmPerHour + 25)));
            Tool("PlumbingSchedule.LessRain", () => _session.SetRainfall(id, Math.Max(20, storm.RainfallMmPerHour - 25)));
        }
        foreach (var outlet in document.PlumbingPoints.Where(point => point.IsSanitaryOutlet || point.Kind == PlumbingPointKind.StormOutlet))
        {
            var id = outlet.Id;
            Tool("PlumbingSchedule.Deeper", () => _session.SetOutletDepth(id, Math.Min(5, outlet.InvertDepth + 0.1)));
            Tool("PlumbingSchedule.Shallower", () => _session.SetOutletDepth(id, Math.Max(0.2, outlet.InvertDepth - 0.1)));
        }
        _content.Children.Add(tools);
        foreach (var point in document.PlumbingPoints)
            _content.Children.Add(new TextBlock
            {
                Text = point.Kind switch
                {
                    PlumbingPointKind.WaterMeter => Format("PlumbingSchedule.MeterLine", point.Name, point.SupplyPressureKPa, point.SupplyPressureKPa / 6.895),
                    PlumbingPointKind.StormOutlet => Format("PlumbingSchedule.StormLine", point.Name, point.RainfallMmPerHour, point.InvertDepth),
                    PlumbingPointKind.VentStack => point.Name,
                    _ => Format("PlumbingSchedule.OutletLine", point.Name, point.InvertDepth)
                },
                Foreground = Brushes.DimGray
            });

        foreach (var report in analysis.Systems)
        {
            var kind = report.System.Kind;
            var unit = Text(kind switch
            {
                PlumbingSystemKind.Sanitary => "Plumbing.DrainUnits",
                PlumbingSystemKind.StormWater => "Plumbing.FlowUnits",
                _ => "Plumbing.SupplyUnits"
            });
            _content.Children.Add(new TextBlock
            {
                Text = Format("PlumbingSchedule.SystemHeader", report.System.Name, Text($"Plumbing.Kind.{kind}"), report.SourceName,
                    report.Fixtures.Count, report.Units, unit, report.DesignFlow, report.MainNominal, report.LengthMeters, StateText(report.Status)),
                FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6),
                Foreground = StateBrush(report.Status)
            });
            var grid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                MinHeight = 60
            };
            void Column(string header, string path, double width) => grid.Columns.Add(new DataGridTextColumn
            {
                Header = Text(header), Binding = new System.Windows.Data.Binding(path), Width = width
            });
            Column("PlumbingSchedule.Fixture", nameof(Row.Fixture), 190);
            Column("PlumbingSchedule.Units", nameof(Row.Units), 70);
            Column("PlumbingSchedule.Branch", nameof(Row.Branch), 70);
            if (kind == PlumbingSystemKind.StormWater)
            {
                Column("PlumbingSchedule.LeaderFlow", nameof(Row.Pressure), 170);
                Column("PlumbingSchedule.Invert", nameof(Row.Invert), 150);
            }
            else if (kind == PlumbingSystemKind.Sanitary)
            {
                Column("PlumbingSchedule.Invert", nameof(Row.Invert), 150);
                Column("PlumbingSchedule.Vent", nameof(Row.Vent), 170);
            }
            else Column("PlumbingSchedule.Pressure", nameof(Row.Pressure), 170);
            if (kind == PlumbingSystemKind.HotWater) Column("PlumbingSchedule.Wait", nameof(Row.Wait), 90);
            Column("PlumbingSchedule.Path", nameof(Row.Path), 80);
            var state = new DataGridTemplateColumn { Header = Text("PlumbingSchedule.State"), Width = 100 };
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Row.State)));
            text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Row.StateBrush)));
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            state.CellTemplate = new DataTemplate { VisualTree = text };
            grid.Columns.Add(state);
            Column("PlumbingSchedule.Note", nameof(Row.Note), 260);
            grid.ItemsSource = report.Fixtures.Select(fixture => new Row(fixture.Name, $"{fixture.Units:0.##}", fixture.Nominal,
                kind == PlumbingSystemKind.StormWater ? $"{fixture.Units:0.00} / {fixture.RequiredKPa:0.00} L/s"
                : double.IsNaN(fixture.PressureKPa) ? string.Empty
                    : $"{fixture.PressureKPa:0} kPa ({fixture.PressureKPa / 6.895:0} psi) / {fixture.RequiredKPa:0}",
                double.IsNaN(fixture.WaitSeconds) ? string.Empty : $"{fixture.WaitSeconds:0} s",
                double.IsNaN(fixture.Invert) ? string.Empty : $"{fixture.Invert:0.00} m ({fixture.Cover * 100:0} cm)",
                $"{fixture.PathLength:0.0} m", StateText(fixture.Status), StateBrush(fixture.Status),
                string.IsNullOrEmpty(fixture.Issue) ? string.Empty : Text(fixture.Issue),
                fixture.Vent.Length == 0 ? string.Empty : $"{fixture.Vent} · {fixture.TrapArm:0.0} / {fixture.TrapArmLimit:0.0} m")).ToList();
            _content.Children.Add(grid);
            foreach (var vent in report.Vents)
                _content.Children.Add(new TextBlock
                {
                    Text = Format("PlumbingSchedule.VentLine", vent.Name, vent.Nominal, vent.Units, vent.Top), Foreground = Brushes.DarkGreen
                });

            var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            var systemId = report.System.Id;
            var rename = new Button { Content = Text("PlumbingSchedule.Rename"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
            rename.Click += (_, _) => Run(() =>
            {
                var name = PromptWindow.Ask(this, Text("PlumbingSchedule.Rename"), report.System.Name);
                if (!string.IsNullOrWhiteSpace(name)) _session.RenamePlumbingSystem(systemId, name);
            });
            var remove = new Button { Content = Text("PlumbingSchedule.Remove"), Padding = new Thickness(10, 3, 10, 3) };
            remove.Click += (_, _) => Run(() => _session.RemovePlumbingSystem(systemId));
            buttons.Children.Add(rename);
            buttons.Children.Add(remove);
            _content.Children.Add(buttons);
        }

        foreach (var storage in analysis.Storage)
            _content.Children.Add(new TextBlock
            {
                Text = storage.Kind == PlumbingPointKind.Cistern
                    ? Format("PlumbingSchedule.CisternLine", storage.Name, storage.Occupants, storage.DailyLiters, storage.CapacityLiters,
                        storage.PeakFlow, storage.PumpRequiredHorsepower, storage.PumpHorsepower,
                        Text(storage.PumpPowered ? "Plumbing.PumpOn" : "Plumbing.PumpNoPower"))
                      + (storage.Issue.Length > 0 ? " · " + Text(storage.Issue) : string.Empty)
                    : Format("PlumbingSchedule.TankLine", storage.Name, storage.Occupants, storage.DailyLiters, storage.CapacityLiters,
                        document.PlumbingPoints.First(point => point.Id == storage.Id).TankHeight)
                      + (storage.Issue.Length > 0 ? " · " + Text(storage.Issue) : string.Empty),
                FontWeight = FontWeights.SemiBold, Foreground = StateBrush(storage.Status), Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        foreach (var septic in analysis.Septic)
            _content.Children.Add(new TextBlock
            {
                Text = Format("PlumbingSchedule.SepticLine", septic.Name, septic.Occupants, septic.DailyLiters, septic.VolumeLiters / 1000,
                    septic.Length, septic.Width, septic.LiquidDepth, septic.FieldArea, septic.FieldLength, septic.Trenches),
                FontWeight = FontWeights.SemiBold, Foreground = Brushes.SaddleBrown, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap
            });
        if (analysis.Roofs.Count > 0 && document.PlumbingPoints.Any(point => point.Kind == PlumbingPointKind.StormOutlet))
        {
            _content.Children.Add(new TextBlock { Text = Text("PlumbingSchedule.Roofs"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var roof in analysis.Roofs)
                _content.Children.Add(new TextBlock
                {
                    Text = Format("PlumbingSchedule.RoofLine", roof.Name, roof.Area, roof.Flow, roof.Downspouts, roof.Capacity, StateText(roof.Status)),
                    Foreground = StateBrush(roof.Status)
                });
        }
        if (analysis.Unserved.Count > 0)
        {
            _content.Children.Add(new TextBlock
            {
                Text = Text("PlumbingSchedule.Unserved"), FontWeight = FontWeights.SemiBold, Foreground = Brushes.Red,
                Margin = new Thickness(0, 12, 0, 4)
            });
            foreach (var (fixture, kind) in analysis.Unserved)
                _content.Children.Add(new TextBlock { Text = $"• {fixture.Name}: {Text($"Plumbing.Kind.{kind}")}", Foreground = Brushes.Red });
        }
        _content.Children.Add(new TextBlock
        {
            Text = Text("PlumbingSchedule.Notes"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0)
        });
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

    private static string StateText(PlumbingStatus status) => Text($"PlumbingStatus.{status}");

    private static Brush StateBrush(PlumbingStatus status) => status switch
    {
        PlumbingStatus.Fail => Brushes.Red,
        PlumbingStatus.Warning => Brushes.DarkOrange,
        PlumbingStatus.Ok => Brushes.SeaGreen,
        _ => Brushes.SlateGray
    };

    private static string Format(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, Text(key), values);

    private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;
}
