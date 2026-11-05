using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;

namespace Guanacaste3D.RevitBridge;

/// <summary>
/// Asks what to export: the whole model, or only what the active 3D view shows.
/// </summary>
internal sealed class ExportDialog : Window
{
    private readonly RadioButton _wholeModel = new() { IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
    private readonly RadioButton _activeView = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _accept = new() { MinWidth = 130, Padding = new Thickness(16, 6, 16, 6), IsDefault = true };
    private readonly View3D? _view;

    private ExportDialog(string title, string action, Document document, View3D? view)
    {
        _view = view;
        Title = "Guanacaste 3D";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var layout = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (BridgeApplication.Icon("export", 32) is { } icon)
            header.Children.Add(new Image { Source = icon, Width = 32, Height = 32, Margin = new Thickness(0, 0, 12, 0) });
        header.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        layout.Children.Add(header);
        layout.Children.Add(new TextBlock
        {
            Text = $"Proyecto: {document.Title}\nMuros, puertas, ventanas, losas y techos se convierten en elementos editables de " +
                   "Guanacaste 3D; muebles, equipos y demás elementos, en familias .g3df.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8), Foreground = Brushes.DimGray
        });
        layout.Children.Add(new TextBlock { Text = "Qué exportar", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        _wholeModel.Content = "Todo el modelo (recomendado)";
        _activeView.Content = view is null
            ? "Solo lo visible en la vista 3D activa (abra una vista 3D para usarlo)"
            : $"Solo lo visible en la vista 3D «{view.Name}»";
        _activeView.IsEnabled = view is not null;
        layout.Children.Add(_wholeModel);
        layout.Children.Add(_activeView);
        _accept.Content = action;
        _accept.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancelar", MinWidth = 100, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(_accept);
        buttons.Children.Add(cancel);
        layout.Children.Add(buttons);
        Content = layout;
    }

    /// <summary>Asks for the scope; null when the user cancels, otherwise the view to limit the export to (or none).</summary>
    public static (bool Accepted, View3D? View) Ask(string title, string action, Document document, View3D? activeView)
    {
        var dialog = new ExportDialog(title, action, document, activeView);
        var accepted = dialog.ShowDialog() == true;
        return (accepted, accepted && dialog._activeView.IsChecked == true ? dialog._view : null);
    }
}

/// <summary>
/// Follows an export. Revit's API only runs on its main thread, so the export runs there and this window repaints
/// between batches of elements.
/// </summary>
internal sealed class ExportProgressWindow : Window
{
    private readonly ProgressBar _progress = new() { Height = 8, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.DimGray };

    public ExportProgressWindow(string projectName)
    {
        Title = "Guanacaste 3D";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var layout = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
        layout.Children.Add(new TextBlock { Text = "Exportando " + projectName, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        layout.Children.Add(_progress);
        layout.Children.Add(_status);
        Content = layout;
    }

    public void Report(double fraction, string? text = null)
    {
        _progress.Value = fraction;
        _status.Text = text ?? $"Leyendo los elementos del modelo… {fraction:P0}";
        // Let WPF paint while Revit's thread is busy exporting.
        Dispatcher.Invoke(DispatcherPriority.Background, () => { });
    }
}
