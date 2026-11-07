using System.Windows;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Navigation;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Resolves a viewport location to the editable element and its render surface category.</summary>
    private (Guid ElementId, SurfaceKind Category)? PickRenderMaterial(Point position, FrameworkElement surface)
    {
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return null;
        var ray = _navigation.PixelRay((float)position.X, (float)position.Y, (float)surface.ActualWidth, (float)surface.ActualHeight);
        if (Picker.Pick(_navigation.Position, ray) is not { } picked) return null;
        SurfaceKind? category = picked.Kind switch
        {
            PickedKind.Wall => _session.Document.Walls.FirstOrDefault(wall => wall.Id == picked.Id) is { } wall
                ? _session.Document.WallTypes.FirstOrDefault(type => type.Id == wall.WallTypeId)?.Construction switch
                {
                    WallConstruction.Brick => SurfaceKind.WallBrick,
                    WallConstruction.Block => SurfaceKind.WallBlock,
                    WallConstruction.Concrete => SurfaceKind.WallConcrete,
                    WallConstruction.Stone => SurfaceKind.WallStone,
                    WallConstruction.Wood => SurfaceKind.WallWood,
                    WallConstruction.Metal => SurfaceKind.WallMetal,
                    _ => SurfaceKind.Wall
                } : SurfaceKind.Wall,
            PickedKind.Floor => _session.Document.Floors.FirstOrDefault(floor => floor.Id == picked.Id) is { } floor
                ? BuildingMesher.SelectableSlabSurface(floor) : SurfaceKind.Floor,
            PickedKind.Opening => _session.Document.Openings.Any(item => item.Id == picked.Id && item.Kind == OpeningKind.Window)
                ? SurfaceKind.Glass : SurfaceKind.DoorLeaf,
            PickedKind.Column or PickedKind.Structural => SurfaceKind.Structure,
            _ => null
        };
        return category is { } kind ? (picked.Id, kind) : null;
    }
}
