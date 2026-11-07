using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Guanacaste3D.Desktop;

/// <summary>A small dialog that asks for one line of text, used to rename views and similar named things.</summary>
internal sealed class PromptWindow : Window
{
    private readonly TextBox _input = new() { MinWidth = 300 };

    private PromptWindow(string title, string initial)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontSize = 12;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        _input.Text = initial;

        var accept = new Button { Content = Text("Dialog.Accept"), MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Text("Dialog.Cancel"), MinWidth = 80, IsCancel = true };
        accept.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(_input);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) =>
        {
            Keyboard.Focus(_input);
            _input.SelectAll();
        };
    }

    /// <summary>Returns what the user typed, or null when the dialog was cancelled.</summary>
    public static string? Ask(Window owner, string title, string initial)
    {
        var window = new PromptWindow(title, initial) { Owner = owner };
        return window.ShowDialog() == true ? window._input.Text : null;
    }

    private static string Text(string key) => Application.Current.TryFindResource(key) as string ?? key;
}
