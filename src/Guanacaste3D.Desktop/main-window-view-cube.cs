namespace Guanacaste3D.Desktop;

/// <summary>Connects the orientation widget to the camera shared by modeling and rendering.</summary>
public partial class MainWindow
{
    /// <summary>Keeps the cube synchronized and routes navigation through the scene navigator.</summary>
    private void InitializeViewCube()
    {
        RegisterModelStyleControls();
        InitializeModelViewControls();
        ViewNavigation.Camera = _navigation;
        ViewNavigation.DirectionRequested += direction => _modelNavigator?.LookFrom(direction);
        ViewNavigation.OrbitRequested += (horizontal, vertical) => _modelNavigator?.OrbitBy(horizontal, vertical);
        ViewNavigation.CommandRequested += ExecuteViewNavigation;
        ViewNavigation.NavigationBar.HistoryAvailability = () => (_modelNavigator?.CanGoBack == true, _modelNavigator?.CanGoForward == true);
        ViewNavigation.NavigationBar.HasPrintFrame = () => _cropActive && _cropFraction is not null;
        _modelNavigator?.Changed += ViewNavigation.Redraw;
        ViewNavigation.Redraw();
    }
}
