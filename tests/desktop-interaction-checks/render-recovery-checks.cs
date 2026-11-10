using System.IO;
using System.Windows;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop;

internal static partial class InteractionChecks
{
    /// <summary>
    /// A studio session that ends without saving leaves a copy; the next start of the render application offers it,
    /// reopens it in the render workspace as unsaved work and reconnects it to its model.
    /// </summary>
    private static void RenderRecoveryChecks()
    {
        var folder = Path.GetFullPath("artifacts/render-recovery");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        var modelPath = Path.Combine(folder, "casa.g3d");
        ProjectFile.Save(modelPath, BreezeBlockSample.Load());

        // First session: open the model, place an object and stop without saving.
        var crashed = new MainWindow { Width = 1200, Height = 800, WindowState = WindowState.Normal };
        _window = crashed;
        Guid placed;
        try
        {
            crashed.EnterRenderWorkspace();
            crashed.Show();
            Pump();
            typeof(MainWindow).GetMethod("AttachDocument", PrivateInstance)!.Invoke(crashed, [ProjectFile.Load(modelPath), modelPath]);
            Pump();
            var session = Field<DocumentSession>("_session");
            placed = session.AddStructuralElement(session.Document.Levels[0].Id, StructuralElementKind.Component, "Unsaved tree",
                new Point2(-5, -5), new Point2(-5, -5));
            Check(Field<bool>("_dirty"), "Placing an object marks the studio project as unsaved");
            crashed.EmergencySave();
            Check(AutoRecovery.Find().Any(copy => copy.DocumentId == session.Document.Id), "An interrupted studio session leaves a recovery copy");
        }
        finally
        {
            // Hidden rather than closed: a crash never runs the normal close that discards the copy.
            crashed.Hide();
        }

        // Next start: the copy is offered and accepted.
        _window = new MainWindow { Width = 1200, Height = 800, WindowState = WindowState.Normal };
        try
        {
            _window.EnterRenderWorkspace();
            _window.Show();
            Pump();
            string? asked = null;
            SetField("_recoveryPromptSink", (Func<string, bool>)(question => { asked = question; return true; }));
            _window.OfferRecovery();
            Pump();
            var session = Field<DocumentSession>("_session");
            Check(asked is not null && session.Document.StructuralElements.Any(element => element.Id == placed),
                "The render application offers the copy at startup and restores the unsaved object");
            Check(Field<bool>("_dirty") && Field<string?>("_filePath") is null,
                "Recovered work stays unsaved until the person chooses where to keep it");
            var tabs = (System.Windows.Controls.TabControl)_window.FindName("ViewTabs");
            Check(tabs.SelectedItem is System.Windows.Controls.TabItem { IsSelected: true } && ((FrameworkElement)_window.FindName("RenderPanelControl")).IsVisible,
                "Recovered work opens in the render workspace");
            ProjectFile.Save(modelPath, BuildingDocument.Create("changed"));
            File.SetLastWriteTimeUtc(modelPath, DateTime.UtcNow.AddSeconds(5));
            Check(_window.ModelUpdateAvailable, "Recovered work is still linked to its model file");
            SaveBitmap(Path.Combine(folder, "01-recovered.png"));
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
            _window = crashed;
            SetField("_dirty", false);
            crashed.Close();
        }
    }
}
