using Guanacaste3D.Core;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private sealed partial class RenderSource
    {
        private int _catalogVersion = -1;
        private IReadOnlyList<RenderSceneItem> _catalog = [];
        public bool HasScene => ModelElementIds(window._session.Document).Any() || window._links.Visible().Any();
        public Guid? SelectedSceneItemId => window.SelectedMovableId();
        public bool CanExecuteSceneCommand(string command)
        {
            var document = window._session.Document;
            var selected = SelectedSceneItemId;
            var wall = window._selectedOpeningId is null && document.Walls.Any(x => x.Id == selected);
            var component = document.StructuralElements.FirstOrDefault(x => x.Id == selected);
            return command switch
            {
                "open" or "save" or "save-as" or "sample" or "import" => true,
                "undo" => window._session.CanUndo,
                "redo" => window._session.CanRedo,
                "delete" => selected is not null && SceneItems().Any(x => x.Id == selected),
                "duplicate" => wall || component?.Kind == StructuralElementKind.Component,
                "rotate" => wall || component is { Kind: StructuralElementKind.Component } && component.Start.DistanceTo(component.End) < 1e-9,
                _ => false
            };
        }
        public bool CanMoveSelection => window.SelectedMovableId() is not null;
        public bool CanElevateSelection => window._selectedStructuralId is not null;

        public string MoveSelection(double x, double y, double z)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
                Math.Abs(x) > 10000 || Math.Abs(y) > 10000 || Math.Abs(z) > 10000)
                return "El desplazamiento debe ser finito y menor de 10 000 m.";
            if (!CanMoveSelection) return "Selecciona un objeto para moverlo.";
            try
            {
                if (z != 0 && window._selectedStructuralId is Guid id)
                    window._session.MoveStructuralElement(id, x, y, z);
                else if (z != 0 || !window.ApplyMove(x, y)) return "El objeto debe permanecer en su superficie anfitriona.";
                window.UpdateModelSelection(true);
                return $"Desplazamiento: X {x:0.##} · Y {y:0.##} · Z {z:0.##} m. Ctrl+Z para deshacer.";
            }
            catch (System.IO.InvalidDataException error) { return LocalizedErrors.Translate(error.Message); }
        }
        public IReadOnlyList<RenderSceneItem> SceneItems()
        {
            if (_catalogVersion == window._sceneVersion) return _catalog;
            var document = window._session.Document;
            var visible = ModelElementIds(window.ModelDisplayDocument()).ToHashSet();
            var layers = document.Levels.ToDictionary(x => x.Id, x => x.Name);
            var items = new List<RenderSceneItem>();
            void Add(Guid id, Guid level, string name, string category) => items.Add(
                new(id, name, category, layers.GetValueOrDefault(level, "Escena"), visible.Contains(id)));
            var index = 0;
            foreach (var wall in document.Walls) Add(wall.Id, wall.LevelId, $"Muro {++index:000}", "Muros");
            index = 0;
            foreach (var opening in document.Openings)
            {
                var level = document.Walls.FirstOrDefault(x => x.Id == opening.WallId)?.LevelId ?? Guid.Empty;
                Add(opening.Id, level, $"{(opening.Kind == OpeningKind.Door ? "Puerta" : "Ventana")} {++index:000}", "Puertas y ventanas");
            }
            index = 0;
            foreach (var floor in document.Floors) Add(floor.Id, floor.LevelId, $"Suelo {++index:000}", "Suelos");
            index = 0;
            foreach (var column in document.Columns) Add(column.Id, column.LevelId, $"Columna {++index:000}", "Columnas");
            foreach (var item in document.StructuralElements) Add(item.Id, item.LevelId, item.Name, "Objetos");
            _catalogVersion = window._sceneVersion;
            return _catalog = items;
        }

        public void SelectSceneItem(Guid id)
        {
            var document = window._session.Document;
            window._viewSelection = null;
            window.SelectExtendedElement();
            if (document.Walls.Any(x => x.Id == id)) window._selectedWallId = id;
            else if (document.Openings.FirstOrDefault(x => x.Id == id) is { } opening)
            {
                window._selectedOpeningId = id;
                window._selectedWallId = opening.WallId;
                window._selectedWallOffset = opening.Offset;
            }
            else if (document.Floors.Any(x => x.Id == id)) window.SelectExtendedElement(floorId: id);
            else if (document.Columns.Any(x => x.Id == id)) window.SelectExtendedElement(columnId: id);
            else if (document.StructuralElements.Any(x => x.Id == id)) window.SelectExtendedElement(structuralId: id);
            if (!App.RenderOnly) window.RefreshProperties();
            window.UpdateModelSelection(true);
        }

        public void SetSceneItemVisible(Guid id, bool visible)
        {
            if (!ModelElementIds(window._session.Document).Contains(id) || window.Model3DView() is not { } view) return;
            var hidden = view.HiddenElementIds.ToHashSet();
            var isolated = view.IsolatedElementIds.ToList();
            if (visible)
            {
                hidden.Remove(id);
                if (isolated.Count > 0 && !isolated.Contains(id)) isolated.Add(id);
                // A hosted opening also needs its wall to be visible.
                if (window._session.Document.Openings.FirstOrDefault(x => x.Id == id) is { } opening)
                {
                    hidden.Remove(opening.WallId);
                    if (isolated.Count > 0 && !isolated.Contains(opening.WallId)) isolated.Add(opening.WallId);
                }
            }
            else hidden.Add(id);
            window._session.SetViewVisibility(view.Id, hidden.ToList(), isolated);
            _catalogVersion = -1;
        }
    }
}
