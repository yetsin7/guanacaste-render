using System.IO;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Routes the Visibility/Graphics command of the View ribbon.</summary>
    private bool TryExecuteViewCategoryCommand(string commandId)
    {
        switch (commandId)
        {
            case "VisibilityGraphics": ShowViewCategoryDialog(); return true;
            case "Filters": EditViewFilters(); return true;
            default: return false;
        }
    }

    /// <summary>
    /// Asks for one filter rule for the active view ("tipo contiene cobogó", "espesor &gt; 0.25"), or for removing the
    /// ones it has ("quitar" removes all, "quitar 2" the second). The walls a rule matches are hidden in that view only.
    /// </summary>
    private void EditViewFilters()
    {
        if (ActiveProjectView() is not { } view)
        {
            StatusText.Text = GetText("Status.NoViewForCategories");
            return;
        }
        var current = view.Filters.Count == 0
            ? GetText("Prompt.Filter.None")
            : string.Join(Environment.NewLine, view.Filters.Select((filter, index) => $"{index + 1}. {filter.Name}"));
        var answer = TextPrompt.Ask(this, GetText("Prompt.Filter.Title") + " · " + view.Name,
            FormatText("Prompt.Filter.Label", current), GetText("Prompt.Accept"), GetText("Prompt.Cancel"));
        if (string.IsNullOrWhiteSpace(answer)) return;
        try
        {
            var remove = GetText("Prompt.Filter.Remove");
            var text = answer.Trim();
            List<ViewFilter> filters;
            if (text.StartsWith(remove, StringComparison.CurrentCultureIgnoreCase))
            {
                var which = text[remove.Length..].Trim();
                if (which.Length == 0) filters = [];
                else if (int.TryParse(which, out var number) && number >= 1 && number <= view.Filters.Count)
                    filters = view.Filters.Where((_, index) => index != number - 1).ToList();
                else
                {
                    StatusText.Text = GetText("Status.FilterNotFound");
                    return;
                }
            }
            else if (ViewFilterParser.TryParse(text) is { } filter) filters = [.. view.Filters, filter];
            else
            {
                StatusText.Text = GetText("Status.FilterNotUnderstood");
                return;
            }
            _session.SetViewFilters(view.Id, filters);
            ApplyViewVisibility();
            StatusText.Text = FormatText("Status.FiltersChanged", filters.Count);
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
        }
    }

    /// <summary>Lets the user choose which categories the active view shows; the rest stay hidden in it.</summary>
    private void ShowViewCategoryDialog()
    {
        if (ActiveProjectView() is not { } view)
        {
            StatusText.Text = GetText("Status.NoViewForCategories");
            return;
        }

        var boxes = Enum.GetValues<ViewCategory>().ToDictionary(category => category, category => new CheckBox
        {
            Content = GetText("ViewCategory." + category),
            IsChecked = !view.HiddenCategories.Contains(category),
            Margin = new Thickness(0, 2, 0, 2)
        });

        var window = new Window
        {
            Title = GetText("Dialog.ViewCategories") + " · " + view.Name,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var accept = new Button { Content = GetText("Prompt.Accept"), IsDefault = true, Width = 90, Margin = new Thickness(0, 12, 8, 0) };
        var cancel = new Button { Content = GetText("Prompt.Cancel"), IsCancel = true, Width = 90, Margin = new Thickness(0, 12, 0, 0) };
        accept.Click += (_, _) => window.DialogResult = true;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        var layout = new StackPanel { Margin = new Thickness(16) };
        layout.Children.Add(new TextBlock { Text = GetText("Dialog.ViewCategories.Hint"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var box in boxes.Values) layout.Children.Add(box);
        layout.Children.Add(buttons);
        window.Content = new ScrollViewer { Content = layout, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        if (window.ShowDialog() != true) return;
        var hidden = boxes.Where(entry => entry.Value.IsChecked != true).Select(entry => entry.Key).ToList();
        _session.SetViewHiddenCategories(view.Id, hidden);
        ApplyViewVisibility();
        StatusText.Text = string.Format(GetText("Status.ViewCategoriesChanged"), hidden.Count);
    }
}
