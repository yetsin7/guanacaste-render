using System.Numerics;
using System.Windows;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop.Controls;

/// <summary>The "Personas" window of the Movie bar: people placed where the view looks, walking routes, drawn in the render and the video.</summary>
public partial class RenderPanel
{
    private PeopleDialog? _peopleWindow;
    private ActorRig? _actorRig;

    /// <summary>
    /// The people and cars of the project as a rig for the preview, rebuilt each time a scene is shown (the scene holds exactly
    /// the actors the rig was made from). Null when there are none.
    /// </summary>
    private void RefreshActorRig() => _actorRig = _source is null ? null : ActorRig.From(_source.GetActors(), CarLibrary.Load);

    /// <summary>
    /// Puts the preview's people and cars where they are at the playhead and lets its fire burn at that second, so playing or
    /// scrubbing the movie in the panel shows them moving. Costs nothing when the project has neither.
    /// </summary>
    private void ApplyMotionPreview()
    {
        if (_preview is null) return;
        if (_actorRig is not null) _preview.SetActors(_actorRig, _playTime);
        if (FiresForFinish() is not null) _preview.SetFinish(CurrentFinish());
        if (WindSlider.Value > 0 && GrassSlider.Value > 0) _preview.SetSettings(PreviewSettings());
    }

    /// <summary>Opens the window of people beside the view (one at a time).</summary>
    private void ShowPeopleWindow()
    {
        if (_source is null) return;
        if (_peopleWindow is { IsLoaded: true })
        {
            _peopleWindow.Activate();
            return;
        }
        var source = _source;
        _peopleWindow = new PeopleDialog(source.GetActors(), PointUnderView, actors =>
        {
            source.SetActors(actors);
            StatusText.Text = actors.Count == 0 ? "No hay personas ni coches en el proyecto." : $"{actors.Count} personas y coches en el proyecto; el render se actualiza.";
        }, GroundNear)
        {
            Owner = Window.GetWindow(this)
        };
        var owner = _peopleWindow.Owner;
        if (owner is not null)
        {
            _peopleWindow.Left = owner.Left + Math.Max(0, owner.ActualWidth - 480);
            _peopleWindow.Top = owner.Top + 90;
        }
        _peopleWindow.Show();
    }

    /// <summary>
    /// The ground round a point, for scattering: from a point a metre and a half above the centre, the first surface below, kept only
    /// if it is about as high as the centre (so nothing lands on a roof, in a tree or down a pit).
    /// </summary>
    private ActorScatter.GroundAt GroundNear(ActorPoint centre)
    {
        // A toposolid gives the slope exactly; elsewhere the ground is found with a ray.
        var terrain = _source?.Terrain();
        return (x, y) => terrain?.HeightAt(x, y) ?? GroundByRay(centre, x, y);
    }

    private double? GroundByRay(ActorPoint centre, double x, double y)
    {
        if (_source?.Query is not { } query) return null;
        var from = new Vector3((float)x, (float)y, (float)centre.Z + 1.5f);
        if (query.Pick(from, -Vector3.UnitZ) is not { } drop) return null;
        var height = from.Z - drop;
        return height >= centre.Z - 1.5 && height <= centre.Z + 0.4 ? height : null;
    }

    /// <summary>
    /// The ground under the centre of the view: the point the view looks at, brought down to the first surface below it, so
    /// a person looked at on a wall or a tree still stands on the floor.
    /// </summary>
    private ActorPoint? PointUnderView()
    {
        if (_source is null) return null;
        var camera = _source.Camera;
        if (_source.Query?.Pick(camera.Position, camera.Forward) is not { } distance) return null;
        var hit = camera.Position + camera.Forward * distance;
        var above = hit + new Vector3(0, 0, 0.3f);
        var height = _source.Query.Pick(above, -Vector3.UnitZ) is { } drop ? above.Z - drop : hit.Z;
        return new ActorPoint(hit.X, hit.Y, height);
    }
}
