using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _groupsWindow;
    private int _groupsSelectedIndex;

    /// <summary>Routes the Create Group, Create Assembly, Detail Group and Load Group buttons.</summary>
    private bool TryExecuteGroupCommand(string commandId)
    {
        switch (commandId)
        {
            case "CreateGroup": CreateGroupFromSelection(ElementGroupKind.Group); return true;
            case "CreateAssembly": CreateGroupFromSelection(ElementGroupKind.Assembly); return true;
            case "DetailGroup": CreateGroupFromSelection(ElementGroupKind.Detail); return true;
            case "LoadGroup": LoadGroupFromFileDialog(); return true;
            default: return false;
        }
    }

    private void CreateGroupFromSelection(ElementGroupKind kind)
    {
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            StatusText.Text = GetText("Status.GroupNothingSelected");
            return;
        }
        var count = _session.Document.ElementGroups.Count(group => group.Kind == kind) + 1;
        var name = TextPrompt.Ask(this, GetText("Group.Title." + kind), GetText("Prompt.Group.Name"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"),
            $"{GetText("Group.Default." + kind)} {count}");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var id = _session.CreateElementGroup(ids, name, kind);
            var created = _session.Document.ElementGroups.First(group => group.Id == id);
            StatusText.Text = FormatText("Status.GroupCreated", created.Name, created.MemberIds.Count);
            RefreshAll();
            ShowGroupsWindow(id);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    private void LoadGroupFromFileDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = GetText("Group.FileFilter") };
        if (dialog.ShowDialog(this) == true) LoadGroupFile(dialog.FileName);
    }

    /// <summary>Loads a saved group onto the active level, asking how far to shift it from where it was saved.</summary>
    private bool LoadGroupFile(string path)
    {
        try
        {
            var shift = TextPrompt.Ask(this, GetText("Group.Load"), GetText("Prompt.Group.Shift"), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), "0 0");
            if (shift is null) return false;
            if (!TryReadShift(shift, out var dx, out var dy))
            {
                StatusText.Text = GetText("Status.GroupShiftInvalid");
                return false;
            }
            var id = _session.LoadElementGroup(File.ReadAllText(path), _activeLevelId, dx, dy);
            var loaded = _session.Document.ElementGroups.First(group => group.Id == id);
            StatusText.Text = FormatText("Status.GroupLoaded", loaded.Name, loaded.MemberIds.Count);
            RefreshAll();
            ShowGroupsWindow(id);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
            return false;
        }
    }

    /// <summary>Reads "x y" (each in the project's unit, or with a unit after it) into meters.</summary>
    private bool TryReadShift(string text, out double dx, out double dy)
    {
        dx = dy = 0;
        var parts = text.Split([(char)32, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && Units.TryParseLength(parts[0], out dx) && Units.TryParseLength(parts[1], out dy);
    }

    private void ShowGroupsWindow(Guid? select = null)
    {
        var groups = _session.Document.ElementGroups;
        if (select is { } wanted) _groupsSelectedIndex = Math.Max(0, groups.FindIndex(group => group.Id == wanted));
        if (_groupsWindow is { IsVisible: true } open)
        {
            BuildGroupsContent();
            open.Activate();
            return;
        }
        var window = NewManageWindow("Group.Manager", 480);
        window.Closed += (_, _) => _groupsWindow = null;
        _groupsWindow = window;
        BuildGroupsContent();
        window.Show();
    }

    private void BuildGroupsContent()
    {
        if (_groupsWindow is null) return;
        var groups = _session.Document.ElementGroups;
        var list = new ListBox { Name = "GroupList", Height = 150, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var group in groups)
            list.Items.Add($"{group.Name} · {GetText("Group.Kind." + group.Kind)} · {FormatText("Group.Members", group.MemberIds.Count)}");
        list.SelectedIndex = groups.Count == 0 ? -1 : Math.Clamp(_groupsSelectedIndex, 0, groups.Count - 1);
        list.SelectionChanged += (_, _) => _groupsSelectedIndex = Math.Max(0, list.SelectedIndex);
        ElementGroup? Current() => list.SelectedIndex >= 0 && list.SelectedIndex < groups.Count ? groups[list.SelectedIndex] : null;

        string? Ask(string key, string? initial = null) =>
            TextPrompt.Ask(this, GetText("Group.Manager"), GetText(key), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), initial);

        bool Run(Action edit, string statusKey, params object[] arguments)
        {
            try
            {
                edit();
                StatusText.Text = arguments.Length == 0 ? GetText(statusKey) : FormatText(statusKey, arguments);
                RefreshAll();
                BuildGroupsContent();
                return true;
            }
            catch (InvalidDataException exception)
            {
                StatusText.Text = LocalizedErrors.Translate(exception.Message);
                return false;
            }
        }

        var first = new WrapPanel();
        first.Children.Add(ManageButton("GroupSelect", GetText("Group.Select"), (_, _) =>
        {
            if (Current() is not { } group) return;
            var ids = _session.GroupElements(group.Id);
            _viewSelection = ids.ToHashSet();
            if (group.MemberIds.Count > 0) SelectElement(group.MemberIds[0]);
            _viewSelection = ids.ToHashSet();
            RenderPlan();
            StatusText.Text = FormatText("Status.GroupSelected", group.Name, ids.Count);
        }));
        first.Children.Add(ManageButton("GroupMove", GetText("Group.Move"), (_, _) =>
        {
            if (Current() is not { } group) return;
            var shift = Ask("Prompt.Group.Shift", "1 0");
            if (shift is null) return;
            if (!TryReadShift(shift, out var dx, out var dy)) { StatusText.Text = GetText("Status.GroupShiftInvalid"); return; }
            Run(() => _session.MoveElementGroup(group.Id, dx, dy), "Status.GroupMoved", group.Name);
        }));
        first.Children.Add(ManageButton("GroupCopy", GetText("Group.Copy"), (_, _) =>
        {
            if (Current() is not { } group) return;
            var shift = Ask("Prompt.Group.Shift", "2 0");
            if (shift is null) return;
            if (!TryReadShift(shift, out var dx, out var dy)) { StatusText.Text = GetText("Status.GroupShiftInvalid"); return; }
            Run(() =>
            {
                var copy = _session.CopyElementGroup(group.Id, dx, dy);
                _groupsSelectedIndex = Math.Max(0, _session.Document.ElementGroups.FindIndex(item => item.Id == copy));
            }, "Status.GroupCopied", group.Name);
        }));
        first.Children.Add(ManageButton("GroupRename", GetText("Group.Rename"), (_, _) =>
        {
            if (Current() is not { } group) return;
            var name = Ask("Prompt.Group.Name", group.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            Run(() => _session.RenameElementGroup(group.Id, name), "Status.GroupRenamed");
        }));

        var second = new WrapPanel();
        second.Children.Add(ManageButton("GroupUngroup", GetText("Group.Ungroup"), (_, _) =>
        {
            if (Current() is not { } group) return;
            Run(() => _session.Ungroup(group.Id), "Status.GroupUngrouped", group.Name);
        }));
        second.Children.Add(ManageButton("GroupDelete", GetText("Group.DeleteMembers"), (_, _) =>
        {
            if (Current() is not { } group) return;
            Run(() => _session.RemoveElementGroupMembers(group.Id), "Status.GroupDeleted", group.Name);
        }));
        second.Children.Add(ManageButton("GroupSave", GetText("Group.Save"), (_, _) =>
        {
            if (Current() is not { } group) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = GetText("Group.FileFilter"), FileName = group.Name + ".g3dgroup" };
            if (dialog.ShowDialog(this) == true) SaveGroupFile(group.Id, dialog.FileName);
        }));
        second.Children.Add(ManageButton("GroupClose", GetText("Phases.Close"), (_, _) => _groupsWindow?.Close()));

        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Group.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(list);
        layout.Children.Add(first);
        layout.Children.Add(second);
        _groupsWindow.Content = layout;
    }

    /// <summary>Writes a group to a file that any project can load.</summary>
    private bool SaveGroupFile(Guid groupId, string path)
    {
        try
        {
            File.WriteAllText(path, _session.WriteElementGroup(groupId));
            StatusText.Text = FormatText("Status.GroupSaved", Path.GetFileName(path));
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = exception is InvalidDataException ? LocalizedErrors.Translate(exception.Message) : exception.Message;
            return false;
        }
    }
}
