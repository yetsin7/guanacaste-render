using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Core.Interop;
using Guanacaste3D.Core.Rendering;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{
    /// <summary>
    /// Follows the whole exchange on the sample house: the modeling application saves a model, the render studio
    /// dresses it, the model changes twice, and each update keeps the render work through save and reopen.
    /// </summary>
    private static void ModelUpdateChecks()
    {
        var folder = Path.GetFullPath("artifacts/model-update");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        var modelPath = Path.Combine(folder, "casa.g3d");
        var projectPath = Path.Combine(folder, "casa-render.gr");
        var model = new DocumentSession(BreezeBlockSample.Load());
        ProjectFile.Save(modelPath, model.Document);

        _window = new MainWindow { Width = 1440, Height = 900, WindowState = WindowState.Normal };
        try
        {
            _window.EnterRenderWorkspace();
            _window.Show();
            Pump();
            var panel = (RenderPanel)_window.FindName("RenderPanelControl");
            T Control<T>(string name) => (T)panel.FindName(name);
            object? Invoke(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(_window, args);
            void WaitForScene()
            {
                var deadline = DateTime.UtcNow.AddSeconds(90);
                Pump();
                while ((Control<Viewport3D>("InteractiveViewport").Children.Count <= 2 ||
                        Control<Border>("InteractiveLoading").Visibility == Visibility.Visible) && DateTime.UtcNow < deadline) Pump();
                for (var settle = 0; settle < 12; settle++) Pump();
            }
            // Evidence pictures show the whole site, so walls added away from the saved camera are in view.
            void Capture(string name)
            {
                WaitForScene();
                Control<Button>("FrameModelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var settle = 0; settle < 12; settle++) Pump();
                SaveBitmap(Path.Combine(folder, name));
            }
            void SaveModel()
            {
                ProjectFile.Save(modelPath, model.Document);
                // File systems with coarse timestamps must still show a later write.
                File.SetLastWriteTimeUtc(modelPath, File.GetLastWriteTimeUtc(modelPath).AddSeconds(3));
                Invoke("RefreshModelUpdateState");
                Pump();
            }

            Invoke("AttachDocument", ProjectFile.Load(modelPath), modelPath);
            WaitForScene();
            var update = Control<Button>("StudioUpdateModelButton");
            Check(update.IsVisible && update.IsEnabled && !panel.ModelUpdatePending,
                "A model opened in the studio shows the update command without a pending change");

            // Render work: a placed tree, a photo, a ground material and the studio's own camera.
            var session = Field<DocumentSession>("_session");
            var level = session.Document.Levels[0].Id;
            var tree = session.AddStructuralElement(level, StructuralElementKind.Component, "Studio tree", new Point2(-4, -3), new Point2(-4, -3));
            var photo = session.AddRenderScene("Fachada", session.Document.RenderSetup ?? new RenderSetup());
            session.SetRenderMaterial(new RenderMaterialAssignment(SurfaceKind.Ground, "builtin:grass") { TextureSize = 2.5 });
            SetField("_filePath", projectPath);
            Check((bool)Invoke("SaveProject", false)! && File.Exists(RenderModelLinkFile.SidecarPath(projectPath)),
                "Saving the render project writes its model link beside it");
            Capture("01-before.png");

            // The architect adds a garden wall and saves the model.
            var corners = model.Document.Walls.SelectMany(wall => new[] { wall.Start, wall.End }).ToArray();
            var minX = corners.Min(point => point.X) - 3;
            var minY = corners.Min(point => point.Y) - 3;
            var gardenWall = model.AddWall(level, new Point2(minX, minY), new Point2(minX + 9, minY), thickness: 0.2);
            SaveModel();
            Check(panel.ModelUpdatePending && _window.ModelUpdateAvailable && (string)update.Content == UiText.Get("ModelUpdate.ButtonPending"),
                "Saving the model in the modeling application announces a pending update in the studio");
            Capture("02-pending.png");

            var wallsBefore = session.Document.Walls.Count;
            update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            session = Field<DocumentSession>("_session");
            Check(session.Document.Walls.Count == wallsBefore + 1 && session.Document.Walls.Any(wall => wall.Id == gardenWall),
                "The update command brings the new wall into the open render project · " + Control<TextBlock>("StatusText").Text);
            Check(session.Document.StructuralElements.Any(element => element.Id == tree) &&
                  session.Document.RenderScenes.Any(scene => scene.Id == photo) &&
                  session.Document.RenderMaterials.Any(material => material.Category == SurfaceKind.Ground && material.TextureSize == 2.5),
                "The update keeps the placed tree, the photo and the render material");
            Check(!panel.ModelUpdatePending && (string)update.Content == UiText.Get("ModelUpdate.Button"),
                "The pending marker clears after the update");
            Capture("03-updated.png");

            session.Undo();
            Check(session.Document.Walls.All(wall => wall.Id != gardenWall), "Undo removes the updated model in one step");
            session.Redo();
            Check(session.Document.Walls.Any(wall => wall.Id == gardenWall), "Redo restores the updated model");

            // Save, reopen and update again: the persisted common model must still tell both sides apart.
            Check((bool)Invoke("SaveProject", false)!, "The updated render project saves");
            Invoke("AttachDocument", ProjectFile.Load(projectPath), projectPath);
            WaitForScene();
            session = Field<DocumentSession>("_session");
            Check(!_window.ModelUpdateAvailable && session.Document.Walls.Any(wall => wall.Id == gardenWall) &&
                  session.Document.StructuralElements.Any(element => element.Id == tree),
                "Reopening the render project restores the model link with nothing pending");
            model.RemoveWall(gardenWall);
            var shed = model.AddWall(level, new Point2(minX, minY - 2), new Point2(minX + 4, minY - 2), thickness: 0.2);
            SaveModel();
            Check(_window.ModelUpdateAvailable, "A second model save is detected after reopening the render project");
            var report = _window.UpdateModelFromSource(interactive: false);
            session = Field<DocumentSession>("_session");
            Check(report is { UsedBaseline: true, ProjectChanged: true } && report.Conflicts.Count == 0 &&
                  session.Document.Walls.All(wall => wall.Id != gardenWall) && session.Document.Walls.Any(wall => wall.Id == shed) &&
                  session.Document.StructuralElements.Any(element => element.Id == tree) &&
                  session.Document.RenderScenes.Any(scene => scene.Id == photo),
                "A second update after reopening removes and adds walls while the render work stays");
            Check(_window.UpdateModelFromSource(interactive: false) is { ProjectChanged: false },
                "Updating again without model changes leaves the project untouched");
            var foreign = Path.Combine(folder, "otra.g3d");
            ProjectFile.Save(foreign, BuildingDocument.Create("Otro proyecto"));
            var walls = session.Document.Walls.Count;
            Check(_window.UpdateModelFromSource(foreign, interactive: false) is null && Field<DocumentSession>("_session").Document.Walls.Count == walls,
                "A model from another project is refused without confirmation");
            Capture("04-second-update.png");

            // The modeling application's own export: opening it links the studio to the model it was taken from.
            var exportPath = Path.Combine(folder, "casa-export.g3d");
            RenderModelExport.Write(exportPath, model.Document.Clone(), modelPath);
            SetField("_dirty", false);
            Invoke("AttachDocument", ProjectFile.Load(exportPath), exportPath);
            WaitForScene();
            session = Field<DocumentSession>("_session");
            var shrub = session.AddStructuralElement(level, StructuralElementKind.Component, "Studio shrub", new Point2(-6, -5), new Point2(-6, -5));
            Check(!_window.ModelUpdateAvailable && (bool)Invoke("SaveProject", false)!,
                "An export opened in the studio is linked to its model and saves in place");
            var fence = model.AddWall(level, new Point2(minX, minY - 4), new Point2(minX + 6, minY - 4), thickness: 0.15);
            SaveModel();
            Check(_window.ModelUpdateAvailable, "Saving the original model is noticed by a studio working on its export");
            var exportReport = _window.UpdateModelFromSource(interactive: false);
            session = Field<DocumentSession>("_session");
            Check(exportReport is { UsedBaseline: true, Added: 1 } && session.Document.Walls.Any(wall => wall.Id == fence) &&
                  session.Document.StructuralElements.Any(element => element.Id == shrub),
                "The studio updates an export straight from the saved model and keeps its own objects");
        }
        catch
        {
            SaveBitmap(Path.Combine(folder, "failure.png"));
            throw;
        }
        finally
        {
            SetField("_dirty", false);
            _window.Close();
        }
    }
}
