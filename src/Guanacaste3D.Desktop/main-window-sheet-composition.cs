using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Interop;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private bool _sheetMenusReady;

    private const int SheetTabIndex = 3;

    /// <summary>True when the sheet drawing is older than the model because the Sheets tab was not open.</summary>
    private bool _sheetStale;

    /// <summary>Draws exactly the same paper-space composition consumed by the PDF exporter.</summary>
    private void RenderSheet()
    {
        if (SheetCanvas is null || _session is null) return;
        if (!_sheetMenusReady)
        {
            _sheetMenusReady = true;
            InitializeSheetNavigation();
            SheetCanvas.MouseLeftButtonDown += SheetCanvas_MouseLeftButtonDown;
            var edit = new MenuItem { Header = GetText("SheetEditor.Open") };
            edit.Click += EditSheet_Click;
            var add = new MenuItem { Header = GetText("Browser.AddSheet") };
            add.Click += AddSheet_Click;
            var print = new MenuItem { Header = GetText("SheetEditor.ExportPdf") };
            print.Click += ExportPdf_Click;
            SheetCanvas.ContextMenu = new ContextMenu { Items = { edit, add, print } };
        }
        // Composing a sheet draws every view placed on it, about a second and a half for the breeze-block house's
        // sheets. It waits until the Sheets tab is open, then draws what the model is now.
        if (ViewTabs.SelectedIndex != SheetTabIndex)
        {
            _sheetStale = true;
            return;
        }
        _sheetStale = false;
        SheetCanvas.Children.Clear();
        var sheet = _session.Document.Sheets.FirstOrDefault(candidate => candidate.Id == _selectedSheetId) ??
            _session.Document.Sheets.FirstOrDefault();
        if (sheet is null) return;
        _selectedSheetId = sheet.Id;
        var composition = SheetComposition.Build(_session.Document, sheet, DateTimeOffset.Now);
        SheetCanvas.Width = composition.Width;
        SheetCanvas.Height = composition.Height;
        SheetCanvas.ClipToBounds = true;
        foreach (var viewport in composition.Viewports)
        {
            var bounds = viewport.Placement;
            var layer = new Canvas { Width = composition.Width, Height = composition.Height,
                Clip = new RectangleGeometry(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height - 14)),
                IsHitTestVisible = false };
            foreach (var graphic in viewport.Graphics) DrawSheetPrimitive(layer, graphic);
            SheetCanvas.Children.Add(layer);
        }
        foreach (var graphic in composition.Graphics) DrawSheetPrimitive(SheetCanvas, graphic);
        DrawGuideGrid(SheetCanvas, composition.Width, composition.Height, sheet.GuideGridMm);
        SheetCanvas.ToolTip = GetText("SheetEditor.Hint");
    }

    /// <summary>Shows sheet identity and physical paper dimensions instead of stale model parameters.</summary>
    private bool TryRefreshSheetProperties()
    {
        if (ViewTabs.SelectedIndex != 3) return false;
        var sheet = _session.Document.Sheets.FirstOrDefault(sheet => sheet.Id == _selectedSheetId);
        if (sheet is null) return false;
        var layout = _session.Document.SheetFamilies.Single(family => family.Id == sheet.TitleBlockFamilyId).SheetLayout!;
        PropertiesText.Text = FormatText("Properties.SheetSummary", sheet.Number, sheet.Name, sheet.PaperSize) +
            Environment.NewLine + FormatText("SheetEditor.PhysicalSize", layout.Width, layout.Height);
        return true;
    }

    /// <summary>Creates paper graphics in millimeters; the surrounding viewbox controls screen magnification.</summary>
    private static void DrawSheetPrimitive(Canvas canvas, SheetPrimitive primitive)
    {
        var color = Color.FromRgb((byte)(primitive.Color.R * 255), (byte)(primitive.Color.G * 255), (byte)(primitive.Color.B * 255));
        var brush = new SolidColorBrush(color);
        if (primitive is SheetText text)
        {
            var label = new TextBlock { Text = text.Text, FontSize = text.Size, Foreground = brush,
                FontFamily = new FontFamily("Arial"),
                FontWeight = text.Bold ? FontWeights.Bold : FontWeights.Normal, IsHitTestVisible = false };
            Canvas.SetLeft(label, text.Position.X);
            Canvas.SetTop(label, text.Position.Y);
            canvas.Children.Add(label);
        }
        else if (primitive is SheetPolygon polygon)
            canvas.Children.Add(new Polygon { Points = new PointCollection(polygon.Points.Select(point => new Point(point.X, point.Y))),
                Stroke = brush, StrokeThickness = polygon.Weight,
                Fill = polygon.Fill is { } fill ? new SolidColorBrush(Color.FromRgb((byte)(fill.R * 255), (byte)(fill.G * 255), (byte)(fill.B * 255))) : null,
                IsHitTestVisible = false });
        else if (primitive is SheetPolyline line)
            canvas.Children.Add(new Polyline { Points = new PointCollection(line.Points.Select(point => new Point(point.X, point.Y))),
                Stroke = brush, StrokeThickness = line.Weight, StrokeDashArray = line.Dashed ? [8, 4] : null, IsHitTestVisible = false });
    }

    /// <summary>Opens composition and family editing directly from a double-click on the drawing paper.</summary>
    private void SheetCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        EditSheet_Click(sender, e);
        e.Handled = true;
    }

    /// <summary>Edits the selected sheet and its shared title-block family through one contextual workspace.</summary>
    private void EditSheet_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _session.Document.Sheets.FirstOrDefault(sheet => sheet.Id == _selectedSheetId) ?? _session.Document.Sheets.FirstOrDefault();
        if (sheet is null) { AddSheet_Click(sender, e); sheet = _session.Document.Sheets.Last(); }
        new SheetEditorWindow(_session, sheet.Id, RefreshAll) { Owner = this }.ShowDialog();
        RefreshAll();
    }
}
