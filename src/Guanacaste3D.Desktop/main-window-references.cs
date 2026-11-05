using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _referencesWindow;
    private readonly Dictionary<Guid, BitmapSource> _underlayImages = [];

    /// <summary>Routes the Insert tab's site imports: terrain points, point clouds, gbXML, images and PDFs.</summary>
    private bool TryExecuteSiteImportCommand(string commandId)
    {
        switch (commandId)
        {
            case "LinkTopography": PickAndRun("SiteImport.PointsFilter", ImportTerrainFile); return true;
            case "PointCloud": PickAndRun("SiteImport.PointsFilter", ImportPointCloudFile); return true;
            case "ImportGbxml": PickAndRun("SiteImport.GbxmlFilter", ImportGbxmlFile); return true;
            case "ImportImage": PickAndRun("SiteImport.ImageFilter", path => { var added = AddImageFile(path); ShowReferencesWindow(); return added; }); return true;
            case "ImportPdf": PickAndRun("SiteImport.PdfFilter", path => { var added = AddPdfFile(path); ShowReferencesWindow(); return added; }); return true;
            default: return false;
        }
    }

    private void PickAndRun(string filterKey, Func<string, bool> action)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = GetText(filterKey) };
        if (dialog.ShowDialog(this) == true) action(dialog.FileName);
    }

    private bool Fail(Exception exception)
    {
        StatusText.Text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
        return false;
    }

    /// <summary>Reads survey points from a text file and makes a terrain of them on the active level.</summary>
    private bool ImportTerrainFile(string path)
    {
        try
        {
            var read = SurveyPointReader.Parse(File.ReadAllText(path));
            var (_, kept) = _session.AddTerrainFromPoints(_activeLevelId, read.Points, Path.GetFileNameWithoutExtension(path));
            StatusText.Text = FormatText("Status.TerrainImported", kept, read.Points.Count, read.Skipped);
            RefreshAll();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) { return Fail(exception); }
    }

    private bool ImportPointCloudFile(string path)
    {
        try
        {
            var read = SurveyPointReader.Parse(File.ReadAllText(path));
            _session.AddPointCloud(_activeLevelId, Path.GetFileNameWithoutExtension(path), read.Points);
            StatusText.Text = FormatText("Status.CloudImported", Math.Min(read.Points.Count, SurveyPointReader.MaximumCloudPoints), read.Points.Count);
            RefreshAll();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) { return Fail(exception); }
    }

    private bool ImportGbxmlFile(string path)
    {
        try
        {
            var made = _session.ImportGbxmlSpaces(_activeLevelId, GbxmlReader.Read(File.ReadAllText(path)));
            StatusText.Text = FormatText("Status.GbxmlImported", made);
            RefreshAll();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) { return Fail(exception); }
    }

    /// <summary>Keeps an image inside the project and lays it in the plan of the active level, asking how wide it really is.</summary>
    private bool AddImageFile(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var decoder = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var width = TextPrompt.Ask(this, GetText("References.Title"), GetText("Prompt.References.Width"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), Units.LengthNumber(20));
            if (string.IsNullOrWhiteSpace(width)) return false;
            if (!Units.TryParseLength(width, out var meters))
            {
                StatusText.Text = GetText("Status.ReferenceWidthInvalid");
                return false;
            }
            using var group = _session.BeginGroup("Transaction.PlaceImageUnderlay");
            var reference = _session.AddReferenceFile(ReferenceKind.Image, Path.GetFileName(path), data, frame.PixelWidth, frame.PixelHeight);
            _session.PlaceImageUnderlay(_activeLevelId, reference, new Point2(0, 0), meters);
            group.Assimilate();
            StatusText.Text = FormatText("Status.ImageImported", Path.GetFileName(path));
            RefreshAll();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        { return Fail(exception); }
    }

    private bool AddPdfFile(string path)
    {
        try
        {
            _session.AddReferenceFile(ReferenceKind.Pdf, Path.GetFileName(path), File.ReadAllBytes(path));
            StatusText.Text = FormatText("Status.PdfAttached", Path.GetFileName(path));
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) { return Fail(exception); }
    }

    /// <summary>Writes a kept PDF to a temporary file and opens it with the program the computer uses for PDFs.</summary>
    private bool OpenReferencePdf(Guid id)
    {
        try
        {
            var file = _session.Document.References.FirstOrDefault(item => item.Id == id && item.Kind == ReferenceKind.Pdf)
                ?? throw new InvalidDataException("The reference file does not exist.");
            var folder = Path.Combine(Path.GetTempPath(), "Guanacaste 3D", "references");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{id:N}-{Path.GetFileName(file.Name)}");
            File.WriteAllBytes(path, file.Data);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return Fail(exception); }
    }

    // ------------------------------------------------------------------ the manager

    private void ShowReferencesWindow()
    {
        if (_referencesWindow is { IsVisible: true })
        {
            BuildReferencesContent();
            _referencesWindow.Activate();
            return;
        }
        var window = NewManageWindow("References.Title", 500);
        window.Closed += (_, _) => _referencesWindow = null;
        _referencesWindow = window;
        BuildReferencesContent();
        window.Show();
    }

    private int _referenceIndex;

    private void BuildReferencesContent()
    {
        if (_referencesWindow is null) return;
        var files = _session.Document.References;
        var list = new ListBox { Name = "ReferenceList", Height = 170, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var file in files)
        {
            var placed = _session.Document.Underlays.Count(item => item.ReferenceId == file.Id);
            list.Items.Add($"{file.Name} · {GetText("ReferenceKind." + file.Kind)} · {file.Data.Length / 1024} KB" +
                           (file.Kind == ReferenceKind.Image ? $" · {FormatText("References.Placed", placed)}" : string.Empty));
        }
        list.SelectedIndex = files.Count == 0 ? -1 : Math.Clamp(_referenceIndex, 0, files.Count - 1);
        list.SelectionChanged += (_, _) => _referenceIndex = Math.Max(0, list.SelectedIndex);
        ReferenceFile? Current() => list.SelectedIndex >= 0 && list.SelectedIndex < files.Count ? files[list.SelectedIndex] : null;

        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("ReferenceAddImage", GetText("References.AddImage"), (_, _) => { PickAndRun("SiteImport.ImageFilter", AddImageFile); BuildReferencesContent(); }));
        buttons.Children.Add(ManageButton("ReferenceAddPdf", GetText("References.AddPdf"), (_, _) => { PickAndRun("SiteImport.PdfFilter", AddPdfFile); BuildReferencesContent(); }));
        buttons.Children.Add(ManageButton("ReferenceOpen", GetText("References.Open"), (_, _) =>
        {
            if (Current() is { Kind: ReferenceKind.Pdf } file) OpenReferencePdf(file.Id);
        }));
        buttons.Children.Add(ManageButton("ReferenceMove", GetText("References.Move"), (_, _) =>
        {
            if (Current() is not { Kind: ReferenceKind.Image } file) return;
            var underlay = _session.Document.Underlays.FirstOrDefault(item => item.ReferenceId == file.Id && item.LevelId == _activeLevelId) ??
                           _session.Document.Underlays.FirstOrDefault(item => item.ReferenceId == file.Id);
            if (underlay is null) { StatusText.Text = GetText("Status.ImageNotPlaced"); return; }
            var current = string.Join(" ", new[] { underlay.Origin.X, underlay.Origin.Y, underlay.Width, underlay.RotationDegrees, underlay.Opacity }
                .Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));
            var text = TextPrompt.Ask(this, GetText("References.Title"), GetText("Prompt.References.Placement"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), current);
            if (string.IsNullOrWhiteSpace(text)) return;
            var parts = text.Split([(char)32, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 5 || parts.Any(part => !TryNumber(part, out _)))
            {
                StatusText.Text = GetText("Status.ImagePlacementInvalid");
                return;
            }
            var numbers = parts.Select(part => { TryNumber(part, out var value); return value; }).ToArray();
            try
            {
                _session.SetImageUnderlay(underlay.Id, new Point2(numbers[0], numbers[1]), numbers[2], numbers[3], numbers[4]);
                StatusText.Text = GetText("Status.ImageMoved");
                RefreshAll();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("ReferencePlace", GetText("References.Place"), (_, _) =>
        {
            if (Current() is not { Kind: ReferenceKind.Image } file) return;
            try
            {
                _session.PlaceImageUnderlay(_activeLevelId, file.Id, new Point2(0, 0), 20);
                StatusText.Text = GetText("Status.ImagePlaced");
                RefreshAll();
                BuildReferencesContent();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("ReferenceRemove", GetText("References.Remove"), (_, _) =>
        {
            if (Current() is not { } file) return;
            try
            {
                _session.RemoveReferenceFile(file.Id);
                _underlayImages.Remove(file.Id);
                StatusText.Text = GetText("Status.ReferenceRemoved");
                RefreshAll();
                BuildReferencesContent();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("ReferenceClose", GetText("Phases.Close"), (_, _) => _referencesWindow?.Close()));

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("References.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(list);
        layout.Children.Add(buttons);
        _referencesWindow.Content = layout;
    }

    // ------------------------------------------------------------------ plan drawing

    private BitmapSource? UnderlayImage(ReferenceFile file)
    {
        if (_underlayImages.TryGetValue(file.Id, out var cached)) return cached;
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(file.Data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return _underlayImages[file.Id] = frame;
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException or InvalidOperationException) { return null; }
    }

    /// <summary>Draws the images and point clouds of the active level, under everything else in the plan.</summary>
    private void RenderSiteReferences()
    {
        foreach (var underlay in _session.Document.Underlays.Where(item => item.LevelId == _activeLevelId))
        {
            var file = _session.Document.References.FirstOrDefault(item => item.Id == underlay.ReferenceId);
            if (file is null || UnderlayImage(file) is not { } source) continue;
            var corner = WorldToCanvas(underlay.Origin);
            var widthPx = underlay.Width * _scale;
            var heightPx = widthPx * file.PixelHeight / file.PixelWidth;
            var image = new Image
            {
                Source = source, Width = widthPx, Height = heightPx, Opacity = underlay.Opacity, IsHitTestVisible = false, Stretch = Stretch.Fill,
                RenderTransform = new RotateTransform(-underlay.RotationDegrees, 0, heightPx)
            };
            Canvas.SetLeft(image, corner.X);
            Canvas.SetTop(image, corner.Y - heightPx);
            PlanCanvas.Children.Add(image);
        }
        foreach (var cloud in _session.Document.PointClouds.Where(item => item.LevelId == _activeLevelId))
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
                foreach (var point in cloud.Points)
                {
                    var at = WorldToCanvas(new Point2(point.X, point.Y));
                    if (at.X < -10 || at.Y < -10 || at.X > PlanCanvas.ActualWidth + 10 || at.Y > PlanCanvas.ActualHeight + 10) continue;
                    context.BeginFigure(at, true, true);
                    context.LineTo(new Point(at.X + 1.5, at.Y), false, false);
                    context.LineTo(new Point(at.X + 1.5, at.Y + 1.5), false, false);
                    context.LineTo(new Point(at.X, at.Y + 1.5), false, false);
                }
            geometry.Freeze();
            PlanCanvas.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Fill = Brushes.DarkSlateBlue, IsHitTestVisible = false });
        }
    }
}
