using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Routes ribbon commands that act on many elements or on the project as a whole, and the ones whose function the
    /// application already had under another name: spaces are rooms, the model check is the warnings review, reports
    /// are the schedules, a curtain system by face is a curtain wall, and so on.
    /// </summary>
    private bool TryExecuteBulkCommand(string commandId)
    {
        switch (commandId)
        {
            case "Scale": ScaleSelectedWall(); return true;
            case "LinearDimension": BeginLinearDimension(); return true;
            case "DetailLine": BeginKindTool(StructuralElementKind.DetailLine); return true;
            case "FilledRegion": BeginKindTool(StructuralElementKind.FilledRegion); return true;
            case "RevisionCloud": BeginKindTool(StructuralElementKind.RevisionCloud); return true;
            case "Insulation": BeginKindTool(StructuralElementKind.Insulation); return true;
            case "AngularDimension": BeginAngularDimension(); return true;
            case "RadialDimension": BeginCurveDimension(PlanDimensionKind.Radius); return true;
            case "DiameterDimension": BeginCurveDimension(PlanDimensionKind.Diameter); return true;
            case "ArcLengthDimension": BeginCurveDimension(PlanDimensionKind.ArcLength); return true;
            case "MaterialTag": ToggleMaterialTags(); return true;
            case "Numbering": RenumberAllRooms(); return true;
            case "FindReplace": FindAndReplaceText(); return true;
            case "ShowHistory": ShowTransactionHistory(); return true;
            case "RestoreBackup": RestoreFromBackup(); return true;
            case "Location": DefineLot(); return true;
            case "ModelCheck": ShowModelWarnings(); return true;
            case "Reports": ShowSchedule_Click(this, new RoutedEventArgs()); return true;
            case "Space": Room_Click(this, new RoutedEventArgs()); return true;
            case "SpaceTag": ShowPlanCategoryTags(TagCategory.Room); return true;
            case "SpaceSeparator": BeginKindTool(StructuralElementKind.RoomSeparator); return true;
            case "SolarAnalysis": ShowSolarStudy(); return true;
            case "CurtainSystem": BeginStructuralTool(StructuralElementKind.CurtainWall, StructuralPlacementMode.Line); return true;
            case "FloorByFace": Floor_Click(this, new RoutedEventArgs()); return true;
            case "SwitchWindows": SwitchToNextView(); return true;
            // The options window holds the interface theme, the canvas color, the view cube and the other settings.
            case "UserInterface":
            case "CanvasTheme":
            case "AdditionalSettings": Options_Click(this, new RoutedEventArgs()); return true;
            default: return false;
        }
    }

    private void ScaleSelectedWall()
    {
        if (_selectedWallId is not Guid id) { StatusText.Text = GetText("Status.SelectWallFirst"); return; }
        var text = TextPrompt.Ask(this, GetText("Prompt.Scale.Title"), GetText("Prompt.Scale.Label"),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"), "2");
        if (text is null) return;
        if (!TryReadNumber(text, out var factor)) { StatusText.Text = GetText("Status.InvalidNumber"); return; }
        var origin = _session.Document.Walls.Single(wall => wall.Id == id).Start;
        RunWallEdit(() => _session.ScaleWalls([id], origin, factor), "Status.WallScaled");
    }

    /// <summary>Two wall clicks make the dimension of the clear distance between them.</summary>
    private void BeginLinearDimension()
    {
        Guid? first = null;
        BeginWallPick(wall =>
        {
            if (first is null)
            {
                first = wall;
                StatusText.Text = GetText("Status.PickDimensionSecond");
                return false;
            }
            var owner = first.Value;
            first = null;
            return RunWallEdit(() => _session.AddDimensionBetween(owner, wall), "Status.DimensionAdded");
        }, "Status.PickDimensionFirst");
    }

    /// <summary>Two wall clicks make the dimension of the angle between those walls.</summary>
    private void BeginAngularDimension()
    {
        Guid? first = null;
        BeginWallPick(wall =>
        {
            if (first is null)
            {
                first = wall;
                StatusText.Text = GetText("Status.PickAngleSecond");
                return false;
            }
            var owner = first.Value;
            first = null;
            return RunWallEdit(() => _session.AddAngularDimension(owner, wall), "Status.DimensionAdded");
        }, "Status.PickAngleFirst");
    }

    /// <summary>One click on a curved wall dimensions its radius, its diameter or its length along the arc.</summary>
    private void BeginCurveDimension(PlanDimensionKind kind) =>
        BeginWallPick(wall => RunWallEdit(() => _session.AddCurveDimension(wall, kind), "Status.DimensionAdded"),
            "Status.PickCurvedWall");

    private void RenumberAllRooms()
    {
        try
        {
            var changed = _session.RenumberRooms();
            RefreshAll();
            StatusText.Text = changed == 0
                ? GetText("Status.RoomsAlreadyNumbered")
                : string.Format(CultureInfo.CurrentCulture, GetText("Status.RoomsRenumbered"), changed);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    private void FindAndReplaceText()
    {
        var find = TextPrompt.Ask(this, GetText("Prompt.Find.Title"), GetText("Prompt.Find.Label"),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
        if (find is null) return;
        var replacement = TextPrompt.Ask(this, GetText("Prompt.Replace.Title"), GetText("Prompt.Replace.Label"),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
        if (replacement is null) return;
        try
        {
            var changed = _session.ReplaceText(find, replacement);
            RefreshAll();
            StatusText.Text = changed == 0
                ? GetText("Status.TextNotFound")
                : string.Format(CultureInfo.CurrentCulture, GetText("Status.TextReplaced"), changed);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    /// <summary>Lists what can be undone, newest first, with the names the person sees in the interface.</summary>
    private void ShowTransactionHistory()
    {
        var names = _session.UndoNames;
        var window = new Window
        {
            Title = GetText("Dialog.History"),
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var layout = new StackPanel { Margin = new Thickness(16) };
        if (names.Count == 0)
            layout.Children.Add(new TextBlock { Text = GetText("Dialog.History.Empty"), Margin = new Thickness(0, 8, 0, 8) });
        else
        {
            var list = new ListBox { MinWidth = 380, MinHeight = 160, MaxHeight = 420 };
            for (var index = 0; index < names.Count; index++)
                list.Items.Add($"{index + 1}. {(TryFindResource(names[index]) as string ?? names[index])}");
            layout.Children.Add(list);
        }
        var close = new Button { Content = GetText("Dialog.Close"), IsCancel = true, IsDefault = true, Width = 90, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close();
        layout.Children.Add(close);
        window.Content = layout;
        window.ShowDialog();
    }

    /// <summary>
    /// Goes back to an earlier state of the project from disk: the copy the last save kept beside the project file, or
    /// else an automatic copy left by a session that did not end normally. Either opens as an unsaved project, so
    /// nothing is overwritten until the person saves.
    /// </summary>
    private void RestoreFromBackup()
    {
        var backup = _filePath is null ? null : _filePath + ".bak";
        if (backup is null || !File.Exists(backup))
        {
            if (AutoRecovery.Find().Count > 0) OfferRecovery();
            else StatusText.Text = GetText("Status.NoBackups");
            return;
        }
        var question = string.Format(CultureInfo.CurrentCulture, GetText("Dialog.RestoreBackup"),
            Path.GetFileName(_filePath), File.GetLastWriteTime(backup).ToString("g", CultureInfo.CurrentCulture));
        var accepted = _recoveryPromptSink is { } sink
            ? sink(question)
            : MessageBox.Show(this, question, "Guanacaste 3D", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (!accepted || !ConfirmDiscard()) return;
        try
        {
            AttachDocument(ProjectFile.Load(backup), null);
            _dirty = true;
            UpdateWindowTitle();
            StatusText.Text = GetText("Status.BackupRestored");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(exception.Message), GetText("Dialog.OpenFailed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Opens the 3D view with shadows on and the sun at midday, to read how the building is lit.</summary>
    private void ShowSolarStudy()
    {
        if (!_tileViews) ViewTabs.SelectedIndex = Model3DTabIndex;
        if (!_modelShadows) ExecuteModelViewCommand("Shadows");
        if (_modelSunPreset == "Off") ExecuteModelViewCommand("Sun.Noon");
        StatusText.Text = GetText("Status.SolarStudy");
    }

    /// <summary>Brings up the next open view, wrapping around after the last one.</summary>
    private void SwitchToNextView()
    {
        if (_tileViews) ExitTileViews();
        ViewTabs.SelectedIndex = (ViewTabs.SelectedIndex + 1) % ViewTabs.Items.Count;
    }
}
