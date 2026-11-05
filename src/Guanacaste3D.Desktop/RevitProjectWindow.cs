using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Import;
using Guanacaste3D.Core.Import.Revit;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Opens a Revit project (.rvt) or a Guanacaste exchange file (.g3dx) as a new editable project: it runs the user's
/// Revit through the bridge add-in when needed, converts the elements, optionally saves every family as a .g3df file,
/// and reports what became native and what kept Revit's geometry before the project is opened.
/// </summary>
public sealed class RevitProjectWindow : Window
{
    private readonly string _path;
    private readonly bool _exchange;
    private readonly ProgressBar _progress = new() { Height = 8, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _report = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 };
    private readonly Border _reportBox;
    private readonly CheckBox _saveFamilies = new() { IsChecked = true, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Button _start = new() { MinWidth = 150, Padding = new Thickness(18, 7, 18, 7), IsDefault = true };
    private readonly Button _cancel = new() { MinWidth = 110, Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(8, 0, 0, 0) };
    private CancellationTokenSource? _cancellation;
    private bool _running;

    /// <summary>The converted project once the user chooses to open it.</summary>
    public BuildingDocument? Result { get; private set; }

    public RevitProjectWindow(string path)
    {
        _path = path;
        _exchange = ExchangeFile.IsExchangePath(path);
        Title = Text("RevitImport.Title");
        Width = 680;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        _report.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        var name = new TextBlock { Text = Path.GetFileName(path), FontSize = 19, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
        titles.Children.Add(name);
        var detail = new TextBlock { Margin = new Thickness(0, 4, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        titles.Children.Add(detail);
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), LineHeight = 19 };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
        titles.Children.Add(intro);
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 12 };
        note.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        titles.Children.Add(note);
        header.Children.Add(titles);

        detail.Text = $"{(_exchange ? "Intercambio Guanacaste 3D" : "Revit")} · {UiText.Bytes(new FileInfo(path).Length)}";
        if (_exchange)
        {
            intro.Text = Text("RevitImport.IntroExchange");
        }
        else
        {
            RevitFileDetails? details = null;
            try { details = RevitFileInfo.Read(path); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { /* the preview is optional */ }
            if (details?.FormatYear is { } year) detail.Text += " · Revit " + year;
            var installation = RevitBridgeRunner.InstallationFor(int.TryParse(details?.FormatYear, out var parsed) ? parsed : null);
            if (installation is null)
            {
                intro.Text = Text("RevitImport.NoRevit");
                _start.IsEnabled = false;
            }
            else
            {
                intro.Text = string.Format(Text("RevitImport.Intro"), installation.Year);
                note.Text = File.Exists(RevitBridgeRunner.CachePath(path)) ? Text("RevitImport.Cached") : Text("RevitImport.FirstRun");
            }
            if (details?.PreviewPng is { Length: > 0 } png && Preview(png) is { } image)
            {
                var frame = new Border
                {
                    Width = 150, Height = 150, Margin = new Thickness(18, 0, 0, 0), Background = Brushes.White, CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1), Child = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(6) }
                };
                frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
                Grid.SetColumn(frame, 1);
                header.Children.Add(frame);
            }
        }
        _saveFamilies.Content = Text("RevitImport.SaveFamilies");
        _saveFamilies.SetResourceReference(ForegroundProperty, "PrimaryTextBrush");

        _reportBox = new Border
        {
            Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed,
            Child = new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _report }
        };
        _reportBox.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        _reportBox.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");

        _start.Content = Text("RevitImport.Start");
        _cancel.Content = Text("Import.Cancel");
        _start.Click += Start_Click;
        _cancel.Click += (_, _) => CancelOrClose();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            CancelOrClose();
            e.Handled = true;
        };
        Closing += (_, e) =>
        {
            // Closing while Revit works would leave it running; cancel first, which also closes Revit.
            if (!_running) return;
            e.Cancel = true;
            CancelOrClose();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(_start);
        buttons.Children.Add(_cancel);
        var layout = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        layout.Children.Add(header);
        layout.Children.Add(_saveFamilies);
        layout.Children.Add(new StackPanel { Margin = new Thickness(0, 16, 0, 0), Children = { _progress, _status } });
        layout.Children.Add(_reportBox);
        layout.Children.Add(buttons);
        Content = layout;
    }

    private void CancelOrClose()
    {
        if (_running) _cancellation?.Cancel();
        else DialogResult = false;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (Result is not null)
        {
            DialogResult = true;
            return;
        }
        _running = true;
        _start.IsEnabled = false;
        _saveFamilies.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
        _cancellation = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        try
        {
            var exchangePath = _path;
            if (!_exchange)
            {
                var progress = new Progress<RevitExportProgress>(ShowStage);
                exchangePath = await RevitBridgeRunner.ExportAsync(_path, progress, _cancellation.Token);
            }
            ShowStage(new RevitExportProgress("converting", 0));
            var saveFamilies = _saveFamilies.IsChecked == true;
            var conversion = await Task.Run(() =>
            {
                var file = ExchangeFile.Read(exchangePath);
                var name = Path.GetFileNameWithoutExtension(_path);
                return ExchangeConverter.Convert(file, name);
            }, _cancellation.Token);
            string? familyFolder = null;
            if (saveFamilies)
            {
                ShowStage(new RevitExportProgress("families", 0));
                familyFolder = await Task.Run(() => SaveFamilies(conversion.Document), _cancellation.Token);
            }
            Result = conversion.Document;
            ShowReport(conversion.Report, familyFolder, clock.Elapsed);
            _start.Content = Text("RevitImport.Open");
            _start.IsEnabled = true;
            _cancel.Content = Text("Dialog.Cancel");
        }
        catch (OperationCanceledException)
        {
            _status.Text = Text("RevitImport.Cancelled");
            _start.IsEnabled = true;
            _saveFamilies.IsEnabled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or
                                       System.ComponentModel.Win32Exception)
        {
            _status.Text = LocalizedErrors.Translate(ex.Message);
            _start.IsEnabled = true;
            _saveFamilies.IsEnabled = true;
        }
        finally
        {
            _running = false;
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowStage(RevitExportProgress stage)
    {
        _progress.IsIndeterminate = stage.Stage != "exporting" || stage.Fraction <= 0;
        _progress.Value = stage.Fraction;
        var key = "RevitImport.Stage." + stage.Stage;
        _status.Text = stage.Stage == "exporting" ? string.Format(Text(key), stage.Fraction) : Text(key);
    }

    /// <summary>Writes every mesh family and every door or window family of the project as .g3df files.</summary>
    private string SaveFamilies(BuildingDocument document)
    {
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path))!,
            Path.GetFileNameWithoutExtension(_path) + " - familias G3D");
        Directory.CreateDirectory(folder);
        foreach (var family in document.ComponentFamilies)
            NativeDocumentStore.SaveFamily(Path.Combine(folder, SafeName(family.Name) + FamilyFile.Extension), family);
        var opening = Path.Combine(folder, "Puertas y ventanas");
        foreach (var name in document.OpeningTypes.Select(type => type.FamilyName).Distinct())
        {
            if (document.OpeningTypes.Where(type => type.FamilyName == name).Select(type => type.Kind).Distinct().Count() != 1) continue;
            Directory.CreateDirectory(opening);
            FamilyFile.Save(Path.Combine(opening, SafeName(name) + FamilyFile.Extension), OpeningFamilyDocument.FromProject(document, name));
        }
        return folder;
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim().TrimEnd('.');
        return safe.Length == 0 ? "Familia" : safe;
    }

    private void ShowReport(ExchangeReport report, string? familyFolder, TimeSpan elapsed)
    {
        _status.Text = string.Format(Text("RevitImport.Done"), elapsed.TotalSeconds);
        var lines = new List<string>
        {
            string.Format(Text("RevitImport.Report.Native"), report.Levels, report.Walls, report.WallTypes, report.Doors, report.Windows,
                report.OpeningTypes, report.Floors, report.Ceilings, report.Roofs, report.Rooms),
            string.Format(Text("RevitImport.Report.Families"), report.Components, report.Families, report.Triangles)
        };
        if (report.MeshFallbacks.Count > 0)
        {
            lines.Add("");
            lines.Add(Text("RevitImport.Report.Fallback"));
            lines.AddRange(report.MeshFallbacks.OrderByDescending(pair => pair.Value)
                .Select(pair => $"  • {pair.Value} {Text("RevitImport.Reason." + pair.Key)}"));
        }
        if (report.Skipped.Count > 0)
        {
            lines.Add("");
            lines.Add(Text("RevitImport.Report.Skipped"));
            lines.AddRange(report.Skipped.OrderByDescending(pair => pair.Value)
                .Select(pair => $"  • {pair.Value} {Text("RevitImport.Reason." + pair.Key)}"));
        }
        if (familyFolder is not null)
        {
            lines.Add("");
            lines.Add(string.Format(Text("RevitImport.Report.FamiliesSaved"), familyFolder));
        }
        _report.Text = string.Join(Environment.NewLine, lines);
        _reportBox.Visibility = Visibility.Visible;
    }

    private static BitmapImage? Preview(byte[] png)
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
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Text(string key) => UiText.Get(key);
}
