using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>The view the next click in the plan will point to, once View Reference has asked for it.</summary>
    private Guid? _pendingViewReference;

    /// <summary>Routes the Matchline, Guide Grid and View Reference buttons of the View tab.</summary>
    private bool TryExecuteSheetAidCommand(string commandId)
    {
        switch (commandId)
        {
            case "Matchline": BeginKindTool(StructuralElementKind.Matchline); return true;
            case "GuideGrid": SetGuideGridFromPrompt(); return true;
            case "ViewReference": BeginViewReference(); return true;
            default: return false;
        }
    }

    /// <summary>Asks for the spacing of the guide grid of the open sheet; 0 removes it.</summary>
    private void SetGuideGridFromPrompt()
    {
        var sheet = _session.Document.Sheets.FirstOrDefault(item => item.Id == _selectedSheetId) ?? _session.Document.Sheets.FirstOrDefault();
        if (sheet is null)
        {
            StatusText.Text = GetText("Status.GuideGridNoSheet");
            return;
        }
        var text = TextPrompt.Ask(this, GetText("GuideGrid.Title"), GetText("Prompt.GuideGrid"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"),
            sheet.GuideGridMm == 0 ? "25" : sheet.GuideGridMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!TryNumber(text, out var spacing))
        {
            StatusText.Text = GetText("Status.GuideGridInvalid");
            return;
        }
        try
        {
            _session.SetSheetGuideGrid(sheet.Id, spacing);
            _selectedSheetId = sheet.Id;
            StatusText.Text = spacing == 0 ? GetText("Status.GuideGridOff") : FormatText("Status.GuideGridOn", spacing);
            RefreshAll();
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    /// <summary>Asks which view to point to (by its name) and arms a click in the plan to put the mark.</summary>
    private void BeginViewReference()
    {
        var names = string.Join(", ", _session.Document.Views.Select(view => view.Name));
        var text = TextPrompt.Ask(this, GetText("ViewReference.Title"), FormatText("Prompt.ViewReference", names), GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
        if (string.IsNullOrWhiteSpace(text)) return;
        if (ViewReferences.Find(_session.Document, text) is not { } view)
        {
            StatusText.Text = GetText("Status.ViewReferenceNotFound");
            return;
        }
        _pendingViewReference = view.Id;
        BeginStructuralTool(StructuralElementKind.ViewReference, StructuralPlacementMode.Point);
        StatusText.Text = FormatText("Status.ViewReferenceArmed", ViewReferences.Label(_session.Document, view));
    }

    /// <summary>Puts the pending view reference where the click landed.</summary>
    private void PlaceViewReference(Point2 point)
    {
        if (_pendingViewReference is not { } viewId) return;
        try
        {
            _selectedStructuralId = _session.AddViewReference(_activeLevelId, point, viewId);
            StatusText.Text = GetText("Status.ViewReferencePlaced");
            RefreshAll();
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    /// <summary>The mark of a view reference in the plan: a circle with a bar, and the text of the view and sheet beside it.</summary>
    private void AddViewReferenceSymbol(StructuralElement element, bool selected)
    {
        var centre = WorldToCanvas(element.Start);
        var brush = selected ? Brushes.DarkOrange : Brushes.SaddleBrown;
        var circle = new Ellipse { Width = 22, Height = 22, Stroke = brush, StrokeThickness = selected ? 2.5 : 1.5, Fill = Brushes.White, Tag = element.Id };
        Canvas.SetLeft(circle, centre.X - 11);
        Canvas.SetTop(circle, centre.Y - 11);
        PlanCanvas.Children.Add(circle);
        PlanCanvas.Children.Add(new Line { X1 = centre.X - 11, Y1 = centre.Y, X2 = centre.X + 11, Y2 = centre.Y, Stroke = brush, StrokeThickness = 1, IsHitTestVisible = false });
        var label = new TextBlock { Text = element.Name, FontSize = 11, Foreground = brush, Tag = element.Id };
        Canvas.SetLeft(label, centre.X + 15);
        Canvas.SetTop(label, centre.Y - 8);
        PlanCanvas.Children.Add(label);
    }

    /// <summary>The guide grid of a sheet, drawn under the sheet's contents on screen only; it is not part of the printed composition.</summary>
    private static void DrawGuideGrid(Canvas canvas, double width, double height, double spacingMm)
    {
        if (spacingMm <= 0) return;
        var brush = new SolidColorBrush(Color.FromArgb(70, 70, 130, 200));
        for (var x = spacingMm; x < width; x += spacingMm)
            canvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = height, Stroke = brush, StrokeThickness = 0.15, IsHitTestVisible = false });
        for (var y = spacingMm; y < height; y += spacingMm)
            canvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = width, Y2 = y, Stroke = brush, StrokeThickness = 0.15, IsHitTestVisible = false });
    }
}
