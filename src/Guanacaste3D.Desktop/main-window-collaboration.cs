using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _collaborationWindow;
    private Window? _worksetsWindow;
    private Window? _requestsWindow;
    private Window? _monitorWindow;
    private int _worksetIndex;

    /// <summary>Routes the Collaborate tab: shared model settings, worksets, synchronization and the monitor.</summary>
    private bool TryExecuteCollaborationCommand(string commandId)
    {
        switch (commandId)
        {
            case "Collaborate": ShowCollaborationWindow(); return true;
            case "Worksets": ShowWorksetsWindow(); return true;
            case "SynchronizeCentral": SynchronizeNow(writeCentral: true); return true;
            case "ReloadLatest": SynchronizeNow(writeCentral: false); return true;
            case "Relinquish": RelinquishAll(); return true;
            case "WorksharingMonitor": ShowMonitorWindow(); return true;
            case "EditRequests": ShowEditRequestsWindow(); return true;
            default: return false;
        }
    }

    /// <summary>Brings this copy and the central model together; reports what came in, what went out and what clashed.</summary>
    private bool SynchronizeNow(bool writeCentral)
    {
        if (!_session.Document.Collaboration.Enabled)
        {
            StatusText.Text = GetText("Status.CollaborationOff");
            return false;
        }
        try
        {
            var report = _session.SynchronizeWithCentral(writeCentral);
            StatusText.Text = report.CreatedCentral
                ? GetText("Status.CentralCreated")
                : FormatText(writeCentral ? "Status.Synchronized" : "Status.Reloaded", report.Merge!.TakenFromCentral, report.Merge.KeptFromMine, report.Merge.Conflicts);
            RefreshAll();
            if (_monitorWindow is { IsVisible: true }) ShowMonitorWindow();
            if (_requestsWindow is { IsVisible: true }) ShowEditRequestsWindow();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
            return false;
        }
    }

    private void RelinquishAll()
    {
        try
        {
            var released = _session.RelinquishWorksets();
            StatusText.Text = released == 0 ? GetText("Status.NothingToRelinquish") : FormatText("Status.Relinquished", released);
            RefreshAll();
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }

    // ------------------------------------------------------------------ settings

    private void ShowCollaborationWindow()
    {
        _collaborationWindow?.Close();
        var settings = _session.Document.Collaboration;
        var window = NewManageWindow("Collaboration.Title", 500);
        window.Closed += (_, _) => _collaborationWindow = null;
        _collaborationWindow = window;
        var user = new TextBox { Name = "CollabUser", Text = settings.User.Length > 0 ? settings.User : Environment.UserName, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
        var suggestion = Path.Combine(string.IsNullOrEmpty(_filePath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(_filePath)!,
            Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(_filePath) ? _session.Document.Name : _filePath) + "-central.g3d");
        var central = new TextBox { Name = "CollabCentral", Text = settings.CentralPath.Length > 0 ? settings.CentralPath : suggestion, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(4, 2, 4, 2) };

        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("CollabBrowse", GetText("Collaboration.Browse"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = GetText("Collaboration.Filter"), FileName = System.IO.Path.GetFileName(central.Text), OverwritePrompt = false };
            if (dialog.ShowDialog(this) == true) central.Text = dialog.FileName;
        }));
        buttons.Children.Add(ManageButton("CollabEnable", GetText("Collaboration.Enable"), (_, _) =>
        {
            try
            {
                _session.EnableCollaboration(user.Text, central.Text);
                StatusText.Text = GetText("Status.CollaborationOn");
                RefreshAll();
            }
            catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
        }));
        buttons.Children.Add(ManageButton("CollabDisable", GetText("Collaboration.Disable"), (_, _) =>
        {
            _session.DisableCollaboration();
            StatusText.Text = GetText("Status.CollaborationDisabled");
            RefreshAll();
        }));
        buttons.Children.Add(ManageButton("CollabSync", GetText("Collaboration.SyncNow"), (_, _) => SynchronizeNow(writeCentral: true)));
        buttons.Children.Add(ManageButton("CollabClose", GetText("Phases.Close"), (_, _) => window.Close()));

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Collaboration.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Text = GetText("Collaboration.User") });
        layout.Children.Add(user);
        layout.Children.Add(new TextBlock { Text = GetText("Collaboration.Central") });
        layout.Children.Add(central);
        layout.Children.Add(buttons);
        window.Content = layout;
        window.Show();
    }

    // ------------------------------------------------------------------ worksets

    private void ShowWorksetsWindow()
    {
        if (_worksetsWindow is { IsVisible: true })
        {
            BuildWorksetsContent();
            _worksetsWindow.Activate();
            return;
        }
        var window = NewManageWindow("Worksets.Title", 500);
        window.Closed += (_, _) => _worksetsWindow = null;
        _worksetsWindow = window;
        BuildWorksetsContent();
        window.Show();
    }

    private void BuildWorksetsContent()
    {
        if (_worksetsWindow is null) return;
        var worksets = _session.Document.Worksets;
        var list = new ListBox { Name = "WorksetList", Height = 160, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var workset in worksets)
            list.Items.Add($"{workset.Name} · {(workset.Owner is null ? GetText("Worksets.Free") : FormatText("Worksets.TakenBy", workset.Owner))} · " +
                           $"{GetText(workset.Visible ? "Worksets.Visible" : "Worksets.Hidden")} · {FormatText("Group.Members", _session.Document.WorksetAssignments.Count(item => item.WorksetId == workset.Id))}");
        list.SelectedIndex = worksets.Count == 0 ? -1 : Math.Clamp(_worksetIndex, 0, worksets.Count - 1);
        list.SelectionChanged += (_, _) => _worksetIndex = Math.Max(0, list.SelectedIndex);
        Workset? Current() => list.SelectedIndex >= 0 && list.SelectedIndex < worksets.Count ? worksets[list.SelectedIndex] : null;

        bool Run(Action edit, string statusKey, params object[] arguments)
        {
            try
            {
                edit();
                StatusText.Text = arguments.Length == 0 ? GetText(statusKey) : FormatText(statusKey, arguments);
                RefreshAll();
                BuildWorksetsContent();
                return true;
            }
            catch (InvalidDataException exception)
            {
                StatusText.Text = LocalizedErrors.Translate(exception.Message);
                return false;
            }
        }

        var first = new WrapPanel();
        first.Children.Add(ManageButton("WorksetNew", GetText("Worksets.New"), (_, _) =>
        {
            var name = TextPrompt.Ask(this, GetText("Worksets.Title"), GetText("Prompt.Workset.Name"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
            if (string.IsNullOrWhiteSpace(name)) return;
            if (Run(() => _session.AddWorkset(name), "Status.WorksetsChanged")) _worksetIndex = _session.Document.Worksets.Count - 1;
            BuildWorksetsContent();
        }));
        first.Children.Add(ManageButton("WorksetRemove", GetText("Worksets.Remove"), (_, _) =>
        {
            if (Current() is { } workset) Run(() => _session.RemoveWorkset(workset.Id), "Status.WorksetsChanged");
        }));
        first.Children.Add(ManageButton("WorksetAssign", GetText("Worksets.Assign"), (_, _) =>
        {
            if (Current() is not { } workset) return;
            var ids = SelectedIds();
            if (ids.Count == 0) { StatusText.Text = GetText("Status.PhaseNoSelection"); return; }
            Run(() => _session.AssignToWorkset(ids, workset.Id), "Status.WorksetAssigned", ids.Count);
        }));
        first.Children.Add(ManageButton("WorksetUnassign", GetText("Worksets.Unassign"), (_, _) =>
        {
            var ids = SelectedIds();
            if (ids.Count == 0) { StatusText.Text = GetText("Status.PhaseNoSelection"); return; }
            Run(() => _session.AssignToWorkset(ids, null), "Status.WorksetAssigned", ids.Count);
        }));
        var second = new WrapPanel();
        second.Children.Add(ManageButton("WorksetToggle", GetText("Worksets.Toggle"), (_, _) =>
        {
            if (Current() is { } workset) Run(() => _session.SetWorksetVisible(workset.Id, !workset.Visible), "Status.WorksetsChanged");
        }));
        second.Children.Add(ManageButton("WorksetBorrow", GetText("Worksets.Borrow"), (_, _) =>
        {
            if (Current() is { } workset) Run(() => _session.BorrowWorkset(workset.Id), "Status.WorksetBorrowed", workset.Name);
        }));
        second.Children.Add(ManageButton("WorksetRelinquish", GetText("Worksets.Relinquish"), (_, _) =>
            Run(() => _session.RelinquishWorksets(), "Status.WorksetsChanged")));
        second.Children.Add(ManageButton("WorksetClose", GetText("Phases.Close"), (_, _) => _worksetsWindow?.Close()));

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Worksets.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(list);
        layout.Children.Add(first);
        layout.Children.Add(second);
        _worksetsWindow.Content = layout;
    }

    // ------------------------------------------------------------------ monitor and requests

    private string MonitorText()
    {
        var status = _session.MonitorCollaboration();
        if (!status.Enabled) return GetText("Monitor.Off");
        var lines = new List<string>
        {
            FormatText("Monitor.User", status.User),
            FormatText("Monitor.Central", status.CentralPath, GetText(status.CentralExists ? "Monitor.Exists" : "Monitor.Missing")),
            FormatText("Monitor.LastSync", status.LastSyncUtc is { } when ? when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : GetText("Monitor.Never")),
            FormatText("Monitor.Local", status.LocalAdded, status.LocalChanged, status.LocalRemoved),
            GetText(status.CentralChangedSince ? "Monitor.CentralNew" : "Monitor.CentralSame")
        };
        lines.AddRange(status.Worksets.Select(workset => $"• {workset.Name}: {(workset.Owner is null ? GetText("Worksets.Free") : FormatText("Worksets.TakenBy", workset.Owner))}"));
        return string.Join(Environment.NewLine, lines);
    }

    private void ShowMonitorWindow()
    {
        _monitorWindow?.Close();
        var window = NewManageWindow("Monitor.Title", 520);
        window.Closed += (_, _) => _monitorWindow = null;
        _monitorWindow = window;
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Name = "MonitorText", Text = MonitorText(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(ManageButton("MonitorRefresh", GetText("Monitor.Refresh"), (_, _) => ShowMonitorWindow()));
        layout.Children.Add(ManageButton("MonitorClose", GetText("Phases.Close"), (_, _) => window.Close()));
        window.Content = layout;
        window.Show();
    }

    private void ShowEditRequestsWindow()
    {
        _requestsWindow?.Close();
        var window = NewManageWindow("Requests.Title", 560);
        window.Closed += (_, _) => _requestsWindow = null;
        _requestsWindow = window;
        var details = _session.LastSync?.Merge?.Details ?? [];
        var list = new ListBox { Name = "RequestList", Height = 180, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var conflict in details)
            list.Items.Add($"{conflict.Collection} · {GetText("Requests." + conflict.Description)} → {GetText("Requests." + conflict.Resolution)}");
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Requests.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Name = "RequestSummary", Margin = new Thickness(0, 0, 0, 6),
            Text = _session.LastSync is null ? GetText("Requests.NoSync") : details.Count == 0 ? GetText("Requests.None") : FormatText("Requests.Count", details.Count) });
        layout.Children.Add(list);
        layout.Children.Add(ManageButton("RequestClose", GetText("Phases.Close"), (_, _) => window.Close()));
        window.Content = layout;
        window.Show();
    }
}
