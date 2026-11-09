using System.IO;
using System.Windows;
using Guanacaste3D.Core;
using Guanacaste3D.Desktop.Controls;

namespace Guanacaste3D.Desktop;

/// <summary>
/// The Home screen: shown at startup and from File &gt; Home, it lists recent projects and samples and starts or opens
/// projects and families. Opening anything returns to the workspace; recent projects keep a cached thumbnail.
/// </summary>
public partial class MainWindow : IStartScreenHost
{
    private readonly RecentDocuments _recents = new();
    private bool _projectChosen;
    private IReadOnlyList<StartSample>? _samples;

    RecentDocuments IStartScreenHost.Recents => _recents;
    bool IStartScreenHost.HasOpenProject => _projectChosen;

    /// <summary>The samples, the breeze-block house first: it opens with its render view ready.</summary>
    IReadOnlyList<StartSample> IStartScreenHost.Samples => _samples ??=
    [
        new("cobogo", "Start.Sample.Cobogo", "Start.Sample.CobogoDetail",
            BreezeBlockSample.Load,
            Path.Combine(AppContext.BaseDirectory, "resources", "samples", "casa-del-cobogo.png")),
        new("villa", "Start.Sample.Villa", "Start.Sample.VillaDetail", ModernHouseShowcase.Create),
        new("two-storey", "Start.Sample.TwoStorey", "Start.Sample.TwoStoreyDetail", TwoStoreyHouse.Create),
        new("demonstration", "Start.Sample.Demonstration", "Start.Sample.DemonstrationDetail", SampleProjects.CreateDemonstrationHouse),
        new("tower", "Start.Sample.Tower", "Start.Sample.TowerDetail", TenStoreyBuilding.Create),
        new("template", "Start.Sample.Template", "Start.Sample.TemplateDetail",
            () => TemplateRules.CreateProject(OfficialTemplates.CreateArchitecturalMetric(), GetText("Project.Untitled")))
    ];

    /// <summary>Shows the Home screen over the workspace.</summary>
    private void ShowStartScreen()
    {
        StartScreenControl.Attach(this);
        StartScreenControl.Visibility = Visibility.Visible;
        StatusText.Text = GetText("Status.StartScreen");
        UpdateWindowTitle();
    }

    private void HideStartScreen()
    {
        StartScreenControl.Visibility = Visibility.Collapsed;
        UpdateWindowTitle();
    }

    /// <summary>Puts a project at the top of the recent list and redraws its thumbnail from the current model.</summary>
    private void RememberRecent(string path, BuildingDocument document)
    {
        _recents.Add(path);
        var thumbnail = _recents.ThumbnailPath(path);
        var copy = document.Clone();
        _ = Task.Run(() => ProjectThumbnail.Faces(copy)).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully) return;
            Dispatcher.BeginInvoke(() =>
            {
                try { ProjectThumbnail.Save(ProjectThumbnail.Render(task.Result), thumbnail); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (StartScreenControl.Visibility == Visibility.Visible) StartScreenControl.Refresh();
            });
        }, TaskScheduler.Default);
    }

    void IStartScreenHost.NewProject() => New_Click(this, new RoutedEventArgs());

    void IStartScreenHost.OpenProjectDialog() => Open_Click(this, new RoutedEventArgs());

    void IStartScreenHost.OpenProject(string path)
    {
        if (!ConfirmDiscard()) return;
        OpenProjectPath(path);
    }

    /// <summary>Door, window and title-block families load into the current project; mesh families are placed in it.</summary>
    void IStartScreenHost.OpenFamilyDialog()
    {
        HideStartScreen();
        ImportFamily_Click(this, new RoutedEventArgs());
    }

    /// <summary>A new door or window family is made in the type manager, from where it is saved as .g3df.</summary>
    void IStartScreenHost.NewFamily()
    {
        HideStartScreen();
        OpeningTypes_Click(this, new RoutedEventArgs());
    }

    void IStartScreenHost.OpenSample(StartSample sample)
    {
        if (!ConfirmDiscard()) return;
        if (sample.Id == "cobogo")
        {
            // The breeze-block house opens on its saved camera, sun and filters, ready to render.
            if (OpenBreezeBlockHouse()) ViewTabs.SelectedIndex = RenderTabIndex;
            return;
        }
        try
        {
            AttachDocument(sample.Build(), null);
            StatusText.Text = FormatText("Status.SampleOpenedNamed", GetText(sample.TitleKey));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.OpenFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void IStartScreenHost.ShowAbout() => About_Click(this, new RoutedEventArgs());

    void IStartScreenHost.ReturnToProject() => HideStartScreen();

    private void Home_Click(object sender, RoutedEventArgs e) => ShowStartScreen();

    /// <summary>Second quick-access button toggles between Home and the current modeling workspace.</summary>
    private void ToggleHome_Click(object sender, RoutedEventArgs e)
    {
        if (StartScreenControl.Visibility == Visibility.Visible)
        {
            if (_projectChosen) HideStartScreen();
            else MessageBox.Show(this, "Abre o crea un proyecto para entrar al espacio de diseño.",
                "Guanacaste 3D", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else ShowStartScreen();
    }
}
