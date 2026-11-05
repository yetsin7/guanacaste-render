using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Guanacaste3D.Core;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

/// <summary>Read-only live preview and CSV export of the selected saved schedule.</summary>
public partial class SchedulesWindow
{
    private const string KindColumn = "__kind";
    private ScheduleTable? _table;

    /// <summary>Evaluates a schedule against the document and shows it with styled group and total rows.</summary>
    private void RefreshPreview(ScheduleDefinition schedule)
    {
        _table = ScheduleEvaluator.Evaluate(_session.Document, schedule, CultureInfo.CurrentCulture,
            Text("Parameters.Yes"), Text("Parameters.No"), _session.Document.Units);
        var data = new DataTable { Locale = CultureInfo.CurrentCulture };
        data.Columns.Add(KindColumn, typeof(ScheduleRowKind));
        PreviewGrid.Columns.Clear();
        var headers = _table.Columns.Select(column => CategoryScheduleService.HeaderWithUnit(
            column.Definition?.Name ?? Text($"Parameters.Column.{column.Key}"), _session.Document.Units.Symbol(column.Unit))).ToList();
        if (_table.HasCountColumn) headers.Add(Text("Schedules.Count"));
        for (var index = 0; index < headers.Count; index++)
        {
            var name = $"c{index}";
            data.Columns.Add(name, typeof(string));
            PreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = headers[index], Binding = new Binding($"[{name}]"), MinWidth = 60,
                Width = index == 0 ? new DataGridLength(1.5, DataGridLengthUnitType.Star) : new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }
        foreach (var row in _table.Rows)
        {
            var cells = new List<object> { row.Kind };
            cells.AddRange(row.Cells.Select(cell => (object)cell));
            while (cells.Count < headers.Count + 1) cells.Add(string.Empty);
            data.Rows.Add([.. cells.Take(headers.Count + 1)]);
        }
        PreviewGrid.ItemsSource = data.DefaultView;
        PreviewTitle.Text = Format("Schedules.PreviewTitle", schedule.Name, _table.ElementCount);
    }

    /// <summary>Styles header, footer, and total rows so groups read like a printed schedule.</summary>
    private void PreviewGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not DataRowView view) return;
        var kind = (ScheduleRowKind)view[KindColumn];
        e.Row.FontWeight = kind == ScheduleRowKind.Element ? FontWeights.Normal : FontWeights.SemiBold;
        e.Row.Background = kind switch
        {
            ScheduleRowKind.GroupHeader => (Brush)FindResource("AccentSoftBrush"),
            ScheduleRowKind.GrandTotal => (Brush)FindResource("ChromeBrush"),
            _ => Brushes.Transparent
        };
        e.Row.Foreground = kind == ScheduleRowKind.GrandTotal ? Brushes.White : (Brush)FindResource("PrimaryTextBrush");
    }

    /// <summary>Exports the measured roof quantities independently of saved schedule definitions.</summary>
    private void ExportRoofQuantities_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.Document.Floors.Any(floor => floor.Kind == FloorKind.Roof))
        {
            StatusText.Text = Text("Schedules.NoRoofs");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = Text("Parameters.CsvFilter"),
            FileName = "cubiertas.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName,
                RoofQuantitySchedule.ToCsv(_session.Document), new System.Text.UTF8Encoding(true));
            StatusText.Text = Format("Parameters.Exported", dialog.FileName);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            StatusText.Text = Format("Parameters.Rejected", LocalizedErrors.Translate(exception.Message));
        }
    }

    /// <summary>
    /// Exports the opening-aware wall layer takeoff without requiring a saved schedule.
    /// The exported areas exclude reveals, joins and paint, as documented by the Core API.
    /// </summary>
    private void ExportWallLayerQuantities_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Document.Walls.Count == 0)
        {
            StatusText.Text = Text("Schedules.NoWalls");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = Text("Parameters.CsvFilter"),
            FileName = "capas-muros.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName,
                WallLayerQuantitySchedule.ToCsv(_session.Document), new System.Text.UTF8Encoding(true));
            StatusText.Text = Format("Parameters.Exported", dialog.FileName);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            StatusText.Text = Format("Parameters.Rejected", LocalizedErrors.Translate(exception.Message));
        }
    }

    /// <summary>Exports an opening-aware, per-type and per-level wall-layer summary.</summary>
    private void ExportWallLayerSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Document.Walls.Count == 0)
        {
            StatusText.Text = Text("Schedules.NoWalls");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = Text("Parameters.CsvFilter"),
            FileName = "resumen-capas-muros.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName,
                WallLayerQuantitySchedule.ToSummaryCsv(_session.Document), new System.Text.UTF8Encoding(true));
            StatusText.Text = Format("Parameters.Exported", dialog.FileName);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            StatusText.Text = Format("Parameters.Rejected", LocalizedErrors.Translate(exception.Message));
        }
    }

    /// <summary>
    /// Exports geometric jamb, head and sill returns by wall layer independently
    /// of saved schedules; this does not estimate applied plaster or trim.
    /// </summary>
    private void ExportWallOpeningReveals_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Document.Openings.Count == 0)
        {
            StatusText.Text = Text("Schedules.NoOpenings");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = Text("Parameters.CsvFilter"),
            FileName = "jambas-huecos-muros.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName,
                WallOpeningRevealSchedule.ToCsv(_session.Document), new System.Text.UTF8Encoding(true));
            StatusText.Text = Format("Parameters.Exported", dialog.FileName);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            StatusText.Text = Format("Parameters.Rejected", LocalizedErrors.Translate(exception.Message));
        }
    }


    /// <summary>Exports grouped geometric returns without claiming installed finish quantities.</summary>
    private void ExportWallOpeningSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Document.Openings.Count == 0)
        {
            StatusText.Text = Text("Schedules.NoOpenings");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = Text("Parameters.CsvFilter"),
            FileName = "resumen-jambas-huecos.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName,
                WallOpeningRevealSchedule.ToSummaryCsv(_session.Document), new System.Text.UTF8Encoding(true));
            StatusText.Text = Format("Parameters.Exported", dialog.FileName);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            StatusText.Text = Format("Parameters.Rejected", LocalizedErrors.Translate(exception.Message));
        }
    }

    /// <summary>Exports the evaluated schedule as CSV.</summary>
    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_table is null || Current is not { } schedule) return;
        var dialog = new SaveFileDialog
        {
            Filter = Text("Parameters.CsvFilter"), FileName = schedule.Name, DefaultExt = ".csv", AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName, ScheduleEvaluator.ToCsv(
                ScheduleEvaluator.Evaluate(_session.Document, schedule, CultureInfo.InvariantCulture)),
                new System.Text.UTF8Encoding(true));
            StatusText.Text = Format("Parameters.Exported", dialog.FileName);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            StatusText.Text = Format("Parameters.Rejected", exception.Message);
        }
    }
}
