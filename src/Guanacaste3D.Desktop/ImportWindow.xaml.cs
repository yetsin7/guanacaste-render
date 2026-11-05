using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core.Import;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Shows what a dropped or chosen file is before it is imported (format, size, Revit version and preview, what is missing
/// to read it) and lets the user set units and orientation. The import runs here, with progress and a working Cancel, and
/// the result, with any warnings, is reported before the window closes.
/// </summary>
public partial class ImportWindow : Window
{
    private readonly string _path;
    private readonly ImportInspection _inspection;
    private readonly LinkedModels _links;
    private readonly Func<(Vector3 Min, Vector3 Max)?> _projectBounds;
    private CancellationTokenSource? _cancellation;
    private bool _finished;

    internal ImportWindow(string path, ImportInspection inspection, LinkedModels links,
        Func<(Vector3 Min, Vector3 Max)?> projectBounds)
    {
        InitializeComponent();
        // Escape cancels a running import first, and only then closes; a Cancel button flagged IsCancel would close at once.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            Cancel_Click(this, e);
            e.Handled = true;
        };
        _path = path;
        _inspection = inspection;
        _links = links;
        _projectBounds = projectBounds;

        FileNameText.Text = Path.GetFileName(path);
        FileDetailText.Text = $"{inspection.FormatName} · {UiText.Bytes(inspection.FileBytes)}";
        SummaryText.Text = UiText.Get(inspection.Summary);
        NotesList.ItemsSource = inspection.Notes.Select(FormatNote).ToList();
        ImportButton.IsEnabled = inspection.CanImport;
        OptionsBox.IsEnabled = inspection.CanImport;
        if (inspection.PreviewPng is { Length: > 0 } png) ShowPreview(png);
        // IFC and CAD files carry their own orientation and units; mesh formats need the user's confirmation more often.
        if (inspection.Format is ImportFormat.Dxf or ImportFormat.Dwg or ImportFormat.Ifc or ImportFormat.Skp)
            UpAxisPicker.SelectedIndex = 1;
    }

    /// <summary>The link created by a successful import, or null.</summary>
    internal ModelLink? Result { get; private set; }

    /// <summary>"Import.Note.X|value" becomes the localized sentence with the value filled in.</summary>
    private static string FormatNote(string note)
    {
        var parts = note.Split('|', 2);
        if (parts.Length != 2) return UiText.Get(parts[0]);
        // Revit writes a few state words in English; show them in Spanish.
        var value = parts[1] switch { "Not enabled" => "no habilitado", "Enabled" => "habilitado", _ => parts[1] };
        return UiText.Format(parts[0], value);
    }

    private void ShowPreview(byte[] png)
    {
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(png);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            PreviewImage.Source = image;
            PreviewBorder.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException)
        {
            // A damaged thumbnail is cosmetic; the rest of the information is still shown.
        }
    }

    private ModelImportOptions BuildOptions()
    {
        double? units = double.TryParse((UnitsPicker.SelectedItem as ComboBoxItem)?.Tag as string, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) ? value : null;
        var up = Enum.TryParse<UpAxis>((UpAxisPicker.SelectedItem as ComboBoxItem)?.Tag as string, out var axis) ? axis : UpAxis.Auto;
        return new ModelImportOptions
        {
            MetersPerUnit = units, UpAxis = up, CenterHorizontally = CenterCheck.IsChecked == true,
            PlaceOnGround = GroundCheck.IsChecked == true
        };
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_finished)
        {
            DialogResult = true;
            return;
        }
        OptionsBox.IsEnabled = false;
        ImportButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        StatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
        StatusText.Text = UiText.Get("Import.Reading");
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<ImportProgress>(p =>
        {
            Progress.IsIndeterminate = false;
            Progress.Value = p.Fraction;
            StatusText.Text = UiText.Get(p.Stage);
        });
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var link = await _links.AddAsync(_path, BuildOptions(), _projectBounds(), progress, _cancellation.Token);
            Result = link;
            var loaded = _links.Get(link.Id)!;
            StatusText.Text = UiText.Format("Import.Imported", link.Name, loaded.Model.TriangleCount, clock.Elapsed.TotalSeconds);
            var warnings = loaded.Model.Warnings.Select(FormatNote).ToList();
            if (warnings.Count > 0) NotesList.ItemsSource = warnings;
            else NotesList.ItemsSource = null;
            Progress.Visibility = Visibility.Collapsed;
            _finished = true;
            ImportButton.Content = UiText.Get("Import.Close");
            ImportButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            // Without warnings there is nothing to read, so close at once.
            if (warnings.Count == 0) DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            Reset();
            StatusText.Text = "";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            Reset();
            StatusText.Foreground = Brushes.Firebrick;
            StatusText.Text = ex is OutOfMemoryException
                ? UiText.Get("Error.The model file is too large to import.")
                : LocalizedErrors.Translate(ex.Message);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void Reset()
    {
        Progress.Visibility = Visibility.Collapsed;
        OptionsBox.IsEnabled = _inspection.CanImport;
        ImportButton.IsEnabled = _inspection.CanImport;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null)
        {
            _cancellation.Cancel();
            return;
        }
        DialogResult = false;
    }
}
