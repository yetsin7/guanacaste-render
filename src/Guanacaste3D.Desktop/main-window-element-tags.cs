using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private bool _showElementTags;
    private bool _showMaterialTags;

    /// <summary>Shows or hides a tag with the material of each wall of the plan.</summary>
    private void ToggleMaterialTags()
    {
        _showMaterialTags = !_showMaterialTags;
        RefreshAll();
        StatusText.Text = GetText(_showMaterialTags ? "Status.MaterialTagsShown" : "Status.MaterialTagsHidden");
    }

    /// <summary>Shows or hides the automatic door, window and column tags of the plan.</summary>
    private void ToggleElementTags()
    {
        _showElementTags = !_showElementTags;
        RefreshAll();
        StatusText.Text = GetText(_showElementTags ? "Status.TagsShown" : "Status.TagsHidden");
    }

    /// <summary>Adds associative wall and opening dimensions to every element of the active level.</summary>
    private void AutoDimensionActiveLevel()
    {
        var added = _session.AutoDimensionLevel(_activeLevelId);
        RefreshAll();
        StatusText.Text = added == 0 ? GetText("Status.AutoDimensionedNone") : FormatText("Status.AutoDimensioned", added);
    }

    /// <summary>Returns the selected plan view, falling back to this level's default plan.</summary>
    private ProjectView? CurrentPlanTagView() =>
        _session.Document.Views.FirstOrDefault(view => view.Id == _selectedViewId &&
            view.LevelId == _activeLevelId && view.Kind == ProjectViewKind.Plan)
        ?? _session.Document.Views.FirstOrDefault(view => view.LevelId == _activeLevelId &&
            view.Kind == ProjectViewKind.Plan);

    /// <summary>Enable persistent associative room/area labels, without duplicating model elements.</summary>
    private void ShowPlanCategoryTags(TagCategory category)
    {
        if (CurrentPlanTagView() is not { } view) return;
        _session.SetPlanTagVisibility(view.Id, category, true);
        RefreshAll();
        var count = category == TagCategory.Room
            ? _session.Document.Rooms.Count(room => room.LevelId == _activeLevelId)
            : _session.Document.StructuralElements.Count(element => element.LevelId == _activeLevelId &&
                element.Kind == StructuralElementKind.Area);
        StatusText.Text = FormatText(category == TagCategory.Room ? "Status.AllRoomsTagged" :
            "Status.AllAreasTagged", count);
    }

    /// <summary>Draws existing model-derived room and area tags as well as temporary opening tags.</summary>
    private void RenderElementTags()
    {
        var selected = CurrentPlanTagView();
        var categories = new HashSet<TagCategory>();
        if (_showElementTags) categories.UnionWith(ElementTags.Default);
        if (_showMaterialTags) categories.Add(TagCategory.Material);
        if (selected?.ShowRoomTags == true) categories.Add(TagCategory.Room);
        if (selected?.ShowAreaTags == true) categories.Add(TagCategory.Area);
        if (categories.Count == 0) return;
        foreach (var tag in ElementTags.Build(_session.Document, _activeLevelId, categories))
        {
            var center = WorldToCanvas(tag.Position);
            var mark = new Border
            {
                Background = Brushes.White, BorderBrush = Brushes.DarkSlateGray, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(3, 0, 3, 0),
                ToolTip = tag.Description, IsHitTestVisible = true,
                Child = new TextBlock
                {
                    Text = tag.Mark, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(mark, center.X - 9);
            Canvas.SetTop(mark, center.Y - 9);
            PlanCanvas.Children.Add(mark);
        }
    }
}
