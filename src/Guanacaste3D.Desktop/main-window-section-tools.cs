using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Guid _sectionViewId = BuiltInProjectViews.SectionAId;
    private bool _creatingSection;
    private Point2? _sectionStart;
    private Point2? _sectionPointer;

    /// <summary>Arms a two-point section tool on the plan without creating a view until both points are accepted.</summary>
    private void BeginSectionTool()
    {
        SetWallTool(false);
        _selectedElectricalId = _selectedPlumbingId = null;
        _creatingSection = true;
        ViewTabs.SelectedIndex = 0;
        PlanCanvas.Cursor = Cursors.Cross;
        Keyboard.Focus(PlanCanvas);
        StatusText.Text = GetText("Documentation.SectionFirstPoint");
    }

    /// <summary>Drops provisional section geometry when another modeling tool or Escape takes over.</summary>
    private void CancelSectionTool()
    {
        _creatingSection = false;
        _sectionStart = _sectionPointer = null;
    }

    /// <summary>Tracks a snapped cut line in model space while the pointer moves.</summary>
    private bool TryUpdateSectionPreview(MouseEventArgs e)
    {
        if (!_creatingSection) return false;
        _sectionPointer = SnapWallPoint(CanvasToWorld(PlanPosition(e)));
        RenderPlan();
        return true;
    }

    /// <summary>Creates a named transverse, longitudinal, or oblique cut from two plan clicks.</summary>
    private bool TryHandleSectionPlacement(MouseButtonEventArgs e)
    {
        if (!_creatingSection) return false;
        e.Handled = true;
        var point = SnapWallPoint(CanvasToWorld(PlanPosition(e)));
        if (_sectionStart is not Point2 start)
        {
            _sectionStart = _sectionPointer = point;
            StatusText.Text = GetText("Documentation.SectionSecondPoint");
            return true;
        }
        var cut = new SectionCut(start, point);
        if (!cut.IsValid)
        {
            StatusText.Text = GetText("Documentation.SectionTooShort");
            return true;
        }
        var number = _session.Document.Views.Count(view => view.Kind == ProjectViewKind.Section) + 1;
        while (_session.Document.Views.Any(view => view.Name == FormatText("Documentation.SectionName", number))) number++;
        var id = _session.AddSectionView(FormatText("Documentation.SectionName", number), cut, SectionDisplayMode.Structural);
        CancelSectionTool();
        PlanCanvas.Cursor = Cursors.Arrow;
        RefreshAll();
        SelectProjectView(id);
        StatusText.Text = GetText("Documentation.SectionCreated");
        return true;
    }

    /// <summary>Draws persistent cut references and the provisional line without consuming model geometry.</summary>
    private void RenderSectionSymbols()
    {
        foreach (var view in _session.Document.Views.Where(view => view.Cut is not null))
            DrawSectionSymbol(view.Cut!, view.Id, view.Name, preview: false);
        if (_creatingSection && _sectionStart is Point2 start && _sectionPointer is Point2 end && start.DistanceTo(end) > 0.01)
            DrawSectionSymbol(new(start, end), Guid.Empty, GetText("Documentation.SectionPreview"), preview: true);
    }

    /// <summary>Draws a section axis with conventional circular heads, contained look arrows, and a view identifier.</summary>
    private void DrawSectionSymbol(SectionCut cut, Guid id, string name, bool preview)
    {
        var start = WorldToCanvas(cut.Start);
        var end = WorldToCanvas(cut.End);
        var brush = preview ? Brushes.RoyalBlue : id == _selectedViewId ? Brushes.DarkOrange : Brushes.DarkSlateGray;
        var menu = preview ? null : CreateViewMenu(id);
        PlanCanvas.Children.Add(new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
            Stroke = brush, StrokeThickness = preview ? 1.2 : 1.8, StrokeDashArray = new DoubleCollection { 12, 4, 2, 4 },
            Tag = id, ContextMenu = menu, ToolTip = name, IsHitTestVisible = !preview });
        var axis = end - start;
        axis.Normalize();
        var look = new Vector(-axis.Y, axis.X);
        var code = ViewMarkerCode(name);
        DrawCircularViewMarker(start, look, id, name, code, brush, menu, !preview);
        DrawCircularViewMarker(end, look, id, name, code, brush, menu, !preview);
        var label = new TextBlock { Text = name, Foreground = brush, Background = Brushes.White,
            FontSize = 11, Tag = id, ContextMenu = menu, IsHitTestVisible = !preview };
        Canvas.SetLeft(label, start.X + look.X * 20 + 8);
        Canvas.SetTop(label, start.Y + look.Y * 20 - 8);
        PlanCanvas.Children.Add(label);
    }

    /// <summary>Selects a cut reference or opens its coordinated section on double-click.</summary>
    private bool TrySelectSectionSymbol(MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.Tag is not Guid id ||
            !_session.Document.Views.Any(view => view.Id == id && view.Kind == ProjectViewKind.Section)) return false;
        _selectedViewId = id;
        _selectedElectricalId = _selectedPlumbingId = null;
        _selectedWallId = _selectedOpeningId = _selectedColumnId = _selectedStructuralId = null;
        _selectedRoomId = _selectedFloorId = null;
        if (e.ClickCount >= 2) SelectProjectView(id);
        else { RefreshProperties(); RenderPlan(); }
        e.Handled = true;
        return true;
    }
}
