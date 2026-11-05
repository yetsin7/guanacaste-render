using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private StackPanel? _documentationEditor;
    private readonly List<TextBox> _documentationCoordinateInputs = [];

    /// <summary>Replaces wall-only fields with datum endpoints while a documentation reference is selected.</summary>
    private void UpdateDocumentationProperties()
    {
        if (PropertiesText.Parent is not StackPanel panel) return;
        var view = _session.Document.Views.FirstOrDefault(view => view.Id == _selectedViewId);
        var axis = _session.Document.Grids.FirstOrDefault(axis => axis.Id == _selectedGridId);
        var sheet = ViewTabs.SelectedIndex == 3 ? _session.Document.Sheets.FirstOrDefault(sheet => sheet.Id == _selectedSheetId) : null;
        var active = view is not null || axis is not null || sheet is not null;
        var startIndex = panel.Children.IndexOf(PropertiesText) + 1;
        for (var index = startIndex; index < panel.Children.Count; index++)
            if (!ReferenceEquals(panel.Children[index], _documentationEditor))
                panel.Children[index].SetValue(VisibilityProperty, active ? Visibility.Collapsed : Visibility.Visible);
        if (_documentationEditor is null)
        {
            _documentationEditor = new StackPanel { Margin = new Thickness(6) };
            panel.Children.Add(_documentationEditor);
        }
        _documentationEditor.Children.Clear();
        _documentationCoordinateInputs.Clear();
        _documentationEditor.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (sheet is not null)
        {
            var edit = new Button { Content = GetText("SheetEditor.Open"), Margin = new Thickness(0, 6, 0, 6) };
            edit.Click += EditSheet_Click;
            _documentationEditor.Children.Add(edit);
            return;
        }
        var cut = axis is not null ? new SectionCut(axis.Start, axis.End) : view?.Cut;
        if (cut is null) return;
        _documentationEditor.Children.Add(new TextBlock { Text = GetText("Documentation.Endpoints"),
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
        var coordinates = new[] { cut.Start.X, cut.Start.Y, cut.End.X, cut.End.Y };
        string[] labels = ["Documentation.StartX", "Documentation.StartY", "Documentation.EndX", "Documentation.EndY"];
        for (var index = 0; index < coordinates.Length; index++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var label = new TextBlock { Text = GetText(labels[index]), Width = 120, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            var input = new TextBox { Text = coordinates[index].ToString("0.###", CultureInfo.CurrentCulture), MinWidth = 65 };
            row.Children.Add(input);
            _documentationCoordinateInputs.Add(input);
            _documentationEditor.Children.Add(row);
        }
        var apply = new Button { Content = GetText("Documentation.ApplyEndpoints"), Tag = axis?.Id ?? view!.Id,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 4) };
        apply.Click += ApplyDocumentationEndpoints_Click;
        _documentationEditor.Children.Add(apply);
    }

    /// <summary>Applies metric endpoints atomically; dependent dimensions and views regenerate from the changed reference.</summary>
    private void ApplyDocumentationEndpoints_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id } || _documentationCoordinateInputs.Count != 4) return;
        var coordinates = new List<double>();
        foreach (var input in _documentationCoordinateInputs)
        {
            if (!double.TryParse(input.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var coordinate) ||
                !double.IsFinite(coordinate))
            {
                StatusText.Text = GetText("Documentation.InvalidCoordinates");
                return;
            }
            coordinates.Add(coordinate);
        }
        var cut = new SectionCut(new(coordinates[0], coordinates[1]), new(coordinates[2], coordinates[3]));
        try
        {
            if (_session.Document.Grids.FirstOrDefault(axis => axis.Id == id) is { } axis)
                _session.UpdateGridAxis(id, axis.Name, cut.Start, cut.End);
            else _session.UpdateSectionCut(id, cut);
            RefreshAll();
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }

    /// <summary>Synchronizes the properties identity when returning to an already open documentation tab.</summary>
    private void SynchronizeDocumentationView()
    {
        if (_session is null || CurrentSelection() is not null || _selectedElectricalId is not null || _selectedPlumbingId is not null) return;
        _selectedGridId = null;
        _selectedViewId = ViewTabs.SelectedIndex switch
        {
            0 => _session.Document.Views.FirstOrDefault(view => view.Kind == ProjectViewKind.Plan && view.LevelId == _activeLevelId)?.Id,
            4 => _elevationViewId,
            5 => _sectionViewId,
            _ => null
        };
        RefreshProperties();
    }

    /// <summary>Refreshes chrome after a generated tab receives its persisted view name.</summary>
    private void UpdateDocumentationCaption()
    {
        if (ActiveViewText is null || ViewTabs.SelectedItem is not TabItem tab) return;
        var name = tab.Header?.ToString() ?? string.Empty;
        ActiveViewText.Text = name;
        if (PropertiesActiveViewText is not null) PropertiesActiveViewText.Text = name;
        UpdateWindowTitle();
    }

    /// <summary>Keeps the active tab, view identity, window caption, and contextual sidebar coordinated.</summary>
    private void ViewTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ViewTabs) || ActiveViewText is null) return;
        // Opening a schedule, sheet, elevation, section or render ends the tiling, whose panes only hold plan and 3D.
        if (_tileViews && ViewTabs.SelectedIndex is not (0 or Model3DTabIndex)) ExitTileViews();
        HighlightTilePane();
        if (_orthographicViewsStale && ViewTabs.SelectedIndex is ElevationTabIndex or SectionTabIndex) RenderOrthographicViews();
        if (_sheetStale && ViewTabs.SelectedIndex == SheetTabIndex) RenderSheet();
        if (_scheduleStale && ViewTabs.SelectedIndex == ScheduleTabIndex) RefreshSchedule();
        UpdateDocumentationCaption();
        SynchronizeDocumentationView();
        UpdateRenderWorkspace(ViewTabs.SelectedIndex == RenderTabIndex);
    }
}
