using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Guanacaste3D.Desktop.Controls;

public partial class RenderPanel
{
    private Point _galleryDragOrigin;

    /// <summary>Begins a material drag only after the pointer crosses the system drag threshold.</summary>
    private void StartGalleryDrag(Button button, GalleryEntry entry, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(button) - _galleryDragOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(button, new DataObject(typeof(GalleryEntry), entry), DragDropEffects.Copy);
    }

    /// <summary>Accepts only materials from this gallery on the interactive scene.</summary>
    private void MaterialDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_rendering && e.Data.GetDataPresent(typeof(GalleryEntry)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Applies the dropped material to the hit element in one undoable project transaction.</summary>
    private void MaterialDrop(object sender, DragEventArgs e)
    {
        if (_rendering || _source is null || e.Data.GetData(typeof(GalleryEntry)) is not GalleryEntry entry) return;
        ApplyDroppedMaterial(entry, e.GetPosition(ImageHost));
        e.Handled = true;
    }

    /// <summary>Resolves and assigns a dragged material at a viewport position, preserving undo history.</summary>
    private bool ApplyDroppedMaterial(GalleryEntry entry, Point position)
    {
        if (_source is null || _rendering) return false;
        if (_source.PickRenderMaterial(position, ImageHost) is not { } target)
        {
            StatusText.Text = UiText.Get("RenderPanel.MaterialDropMiss");
            return false;
        }
        if (PreviewToggle.IsChecked == true) PreviewToggle.IsChecked = false;
        _showingFinal = false;
        ShowInteractiveScene(true);
        _source.AssignRenderMaterial(target.Category, entry.Key, entry.Size, entry.Relief, target.ElementId);
        FocusMaterial(target.Category, target.ElementId);
        StatusText.Text = UiText.Get("RenderPanel.MaterialDropApplied");
        return true;
    }
}
