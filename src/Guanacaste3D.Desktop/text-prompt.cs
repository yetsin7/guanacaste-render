using System.Windows;
using System.Windows.Controls;

namespace Guanacaste3D.Desktop;

/// <summary>Asks for one line of text in a small modal dialog owned by the main window.</summary>
internal static class TextPrompt
{
    /// <summary>Answers instead of the dialog, for non-interactive WPF checks. Real users always see the dialog.</summary>
    internal static Func<string, string?>? Responder;

    /// <summary>Told every answer given, so the automation recorder can keep what a command asked.</summary>
    internal static Action<string>? Recorder;

    /// <summary>Returns the trimmed text, or null when the user cancels or leaves the field blank.</summary>
    public static string? Ask(Window owner, string title, string label, string acceptText, string cancelText,
        string? initialText = null)
    {
        if (Responder is { } responder)
        {
            var answer = responder(title);
            if (answer is not null) Recorder?.Invoke(answer);
            return answer;
        }
        var input = new TextBox { MinWidth = 260, Text = initialText ?? string.Empty };
        input.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        string? result = null;
        var window = new Window
        {
            Title = title,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var accept = new Button { Content = acceptText, IsDefault = true, Width = 90, Margin = new Thickness(0, 12, 8, 0) };
        var cancel = new Button { Content = cancelText, IsCancel = true, Width = 90, Margin = new Thickness(0, 12, 0, 0) };
        accept.Click += (_, _) =>
        {
            result = input.Text;
            window.DialogResult = true;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        var layout = new StackPanel { Margin = new Thickness(16) };
        layout.Children.Add(new TextBlock { Text = label });
        layout.Children.Add(input);
        layout.Children.Add(buttons);
        window.Content = layout;

        window.ShowDialog();
        var accepted = window.DialogResult == true && !string.IsNullOrWhiteSpace(result) ? result.Trim() : null;
        if (accepted is not null) Recorder?.Invoke(accepted);
        return accepted;
    }
}
