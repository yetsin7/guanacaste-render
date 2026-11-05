using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Geometry;
using Guanacaste3D.Desktop;
using Guanacaste3D.Desktop.Controls;

internal static partial class InteractionChecks
{

    /// <summary>Opens each registered document kind through the same path used by Windows file associations.</summary>
    private static void CheckAssociatedDocumentStartup()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"g3d-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var projectPath = Path.Combine(folder, "startup.g3d");
            ProjectFile.Save(projectPath, BuildingDocument.Create("StartupProject"));
            var familyPath = Path.Combine(folder, "startup.g3df");
            FamilyFile.Save(familyPath, OpeningFamilyDocument.CreateStandard(OpeningKind.Door, "StartupFamily"));
            var templatePath = Path.Combine(folder, "startup.g3dt");
            TemplateFile.Save(templatePath, OfficialTemplates.CreateArchitecturalMetric());

            foreach (var path in new[] { projectPath, familyPath, templatePath })
            {
                var opened = new MainWindow(path);
                var loaded = (DocumentSession)typeof(MainWindow).GetField("_session", PrivateInstance)!.GetValue(opened)!;
                if (path == projectPath)
                    Check(loaded.Document.Name == "StartupProject", "Associated .g3d loads the project");
                else if (path == familyPath)
                    Check(loaded.Document.OpeningTypes.Any(type => type.FamilyName == "StartupFamily"),
                        "Associated .g3df imports the family");
                else
                    Check(loaded.Document.Levels.Count > 0 &&
                        typeof(MainWindow).GetField("_filePath", PrivateInstance)!.GetValue(opened) is null,
                        "Associated .g3dt creates an unsaved project");
                typeof(MainWindow).GetField("_dirty", PrivateInstance)!.SetValue(opened, false);
                opened.Close();
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Every ribbon command has an icon of its own, and a contact sheet of all of them (large and at ribbon size) is written
    /// for visual review.
    /// </summary>
    private static void IconChecks()
    {
        var ids = Enum.GetValues<Guanacaste3D.Desktop.Controls.RibbonWorkspace>()
            .SelectMany(workspace => Guanacaste3D.Desktop.Controls.RibbonCatalog.GetGroups(workspace))
            .SelectMany(group => group.Commands).Select(command => command.CommandId).Distinct().ToList();
        var resources = Application.Current.Resources;
        var missing = ids.Where(id => resources["CmdIcon." + id] is not ImageSource).ToList();
        Check(ids.Count > 150 && missing.Count == 0, "Every ribbon command has its own icon" + (missing.Count > 0 ? ": " + string.Join(", ", missing) : ""));
        IEnumerable<string> KeysOf(ResourceDictionary dictionary) =>
            dictionary.Keys.OfType<string>().Concat(dictionary.MergedDictionaries.SelectMany(KeysOf));
        var icons = KeysOf(resources).Where(key => key.StartsWith("CmdIcon.")).Distinct().OrderBy(key => key).ToList();
        const int columns = 14;
        var rows = (icons.Count + columns - 1) / columns;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, columns * 92, rows * 92));
            for (var index = 0; index < icons.Count; index++)
            {
                var x = index % columns * 92;
                var y = index / columns * 92;
                var image = (ImageSource)resources[icons[index]];
                context.DrawImage(image, new Rect(x + 6, y + 6, 48, 48));
                context.DrawImage(image, new Rect(x + 62, y + 6, 16, 16));
                context.DrawImage(image, new Rect(x + 62, y + 28, 24, 24));
                var label = new FormattedText(icons[index][8..], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 9, Brushes.DimGray, 1.0) { MaxTextWidth = 86, MaxTextHeight = 24 };
                context.DrawText(label, new Point(x + 3, y + 60));
            }
        }
        var sheet = new RenderTargetBitmap(columns * 92, rows * 92, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        using var stream = File.Create("artifacts/interaction-evidence/command-icons.png");
        encoder.Save(stream);
    }

    /// <summary>Verifies that demonstrations require an explicit request, then captures the Door scene.</summary>
    private static void CaptureDemoToolTip()
    {
        var door = FindButtons(_window).First(button => button.Tag as string == "Door");
        var ribbon = (DependencyObject)_window.FindName("RibbonTabControl");
        var allRibbonButtons = FindButtons(ribbon).Where(button => button.Tag is string).ToList();
        Check(door.ToolTip is ToolTip && allRibbonButtons.Count > 10 && allRibbonButtons.All(button => button.ToolTip is ToolTip),
            "Every ribbon button carries an animated demonstration tooltip");
        Check(new[] { "Door", "Window", "Wall", "Move", "Column", "Room", "Render", "Levels", "Schedules", "ManageLinks" }
            .Select(CommandDemoToolTip.KindFor).All(kind => kind != CommandDemoKind.None) &&
            CommandDemoToolTip.KindFor("Levels") == CommandDemoKind.OpenPanel && CommandDemoToolTip.KindFor("Move") == CommandDemoKind.MoveElement,
            "Tools and dialogs alike are mapped to a demonstration scene");
        var tip = CommandDemoToolTip.CreateDemo("Door", (string)door.Content);
        var ordinaryTip = new ToolTip { Content = door.Content };
        ordinaryTip.SetResourceReference(FrameworkElement.StyleProperty, typeof(ToolTip));
        Check(ordinaryTip.StaysOpen, "Ordinary tooltips inherit a style compatible with WPF automatic opening");
        tip.PlacementTarget = door;
        // Tooltips only open over the active window; another application in front must not decide the result.
        _window.Activate();
        Pump();
        tip.IsOpen = true;
        for (var frame = 0; frame < 30; frame++) Pump();
        var content = (FrameworkElement)tip.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create("artifacts/interaction-evidence/door-demo-tooltip.png")) encoder.Save(stream);
        Check(content.ActualHeight > 150, "Door demonstration scene is laid out inside the tooltip");
        Check(tip.StaysOpen && tip.Placement == System.Windows.Controls.Primitives.PlacementMode.Custom &&
              tip.CustomPopupPlacementCallback is not null,
            "Tooltips remain compatible with WPF automatic reopening while staying inside their active owner");
        var competingWindow = new Window { Width = 320, Height = 200, ShowInTaskbar = false };
        competingWindow.Show();
        competingWindow.Activate();
        Pump();
        Check(!tip.IsOpen, "Tooltips close when Guanacaste 3D loses activation");
        competingWindow.Close();
        _window.Activate();
        Pump();
        var automaticTip = (ToolTip)door.ToolTip;
        automaticTip.PlacementTarget = door;
        automaticTip.IsOpen = true;
        Pump();
        automaticTip.IsOpen = false;
        Pump();
        Check(automaticTip.StaysOpen, "A closed ribbon tooltip remains valid for the next automatic hover");
    }

    /// <summary>Finds every button in the window's logical tree.</summary>
    /// <summary>Pumps the dispatcher until a condition holds or the time runs out.</summary>
    private static bool WaitFor(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            Pump();
            if (condition()) return true;
        }
        return condition();
    }

    /// <summary>Enumerates command buttons across nested logical controls for tooltip coverage.</summary>
    private static IEnumerable<Button> FindButtons(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }
}
