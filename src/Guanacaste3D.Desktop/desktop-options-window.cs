using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

/// <summary>Revit-inspired options dialog with settings stored for the current Windows user.</summary>
public sealed class DesktopOptionsWindow : Window
{
    private readonly DesktopOptions _options;
    private readonly StackPanel _page = new() { Margin = new Thickness(16, 12, 18, 16) };
    private readonly ListBox _sections = new();
    private readonly Action<DesktopOptions> _apply;

    private static readonly string[] Pages =
    [
        "General", "Interfaz de usuario", "Colores", "Gráficos", "Hardware",
        "Ubicaciones de archivos", "Renderización", "Revisar la ortografía",
        "SteeringWheels", "ViewCube", "Macros", "Modelo en la nube"
    ];

    public DesktopOptionsWindow(Window owner, Action<DesktopOptions> apply)
    {
        Owner = owner;
        _apply = apply;
        _options = DesktopOptions.Load();
        Title = "Opciones — Guanacaste 3D";
        Width = 745;
        Height = 805;
        MinWidth = 650;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new Grid { Margin = new Thickness(8, 8, 8, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(186) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _sections.BorderBrush = Brushes.LightGray;
        _sections.BorderThickness = new Thickness(1);
        _sections.SelectionChanged += (_, _) => ShowPage();
        foreach (var page in Pages) _sections.Items.Add(page);
        body.Children.Add(_sections);

        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        viewer.Content = _page;
        Grid.SetColumn(viewer, 1);
        body.Children.Add(viewer);
        layout.Children.Add(body);

        var footer = new DockPanel { Margin = new Thickness(8), LastChildFill = false };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Aceptar", Width = 86, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (!_options.Save())
            {
                MessageBox.Show(this, "No se pudieron guardar las preferencias.", "Guanacaste 3D",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _apply(_options);
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancelar", Width = 86, IsCancel = true };
        var help = new Button { Content = "Ayuda", Width = 86 };
        help.Click += (_, _) => MessageBox.Show(this,
            "Las opciones se guardan por usuario en AppData local. Los cambios gráficos compatibles se aplican al aceptar; algunas opciones avanzadas requerirán integración futura con el motor.",
            "Ayuda de Opciones", MessageBoxButton.OK, MessageBoxImage.Information);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        buttons.Children.Add(help);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 1);
        layout.Children.Add(footer);
        Content = layout;
        _sections.SelectedIndex = 0;
    }

    private void ShowPage()
    {
        _page.Children.Clear();
        switch (_sections.SelectedIndex)
        {
            case 0:
                Group("Notificaciones", p =>
                {
                    Choice(p, "Recordatorio para guardar", _options.SaveReminderMinutes.ToString(),
                        ["0", "5", "10", "15", "30", "60"],
                        s => _options.SaveReminderMinutes = int.Parse(s), " minutos");
                });
                Group("Nombre de usuario", p => Note(p, Environment.UserName));
                Group("Opciones de visualización", p =>
                    Choice(p, "Disciplina de vista por defecto", _options.DefaultViewDiscipline,
                        ["Arquitectura", "Estructura", "Coordinación", "Sistemas"],
                        s => _options.DefaultViewDiscipline = s));
                break;
            case 1:
                Group("Pantalla de inicio", p => Check(p, "Activar lista de archivos recientes en el inicio",
                    _options.ShowRecentFiles, b => _options.ShowRecentFiles = b));
                Group("Cinta de opciones", p => Check(p, "Mostrar ficha contextual al seleccionar",
                    _options.ShowContextualTab, b => _options.ShowContextualTab = b));
                Group("Personalización", p =>
                {
                    var edit = new Button { Content = "Teclas de acceso rápido: Personalizar...", HorizontalAlignment = HorizontalAlignment.Left };
                    edit.Click += (_, _) => new ShortcutOptionsWindow(this, _options.Shortcuts).ShowDialog();
                    p.Children.Add(edit);
                });
                break;
            case 2:
                Group("Apariencia", p => Choice(p, "Tema activo", _options.UiTheme,
                    ["Claro", "Oscuro"], s => _options.UiTheme = s));
                Group("Colores del lienzo", p =>
                {
                    ColorField(p, "Fondo", _options.CanvasColor, s => _options.CanvasColor = s);
                    ColorField(p, "Selección", _options.SelectionColor, s => _options.SelectionColor = s);
                    ColorField(p, "Advertencia", _options.WarningColor, s => _options.WarningColor = s);
                });
                break;
            case 3:
                Group("Rendimiento de navegación de vistas", p =>
                    Check(p, "Simplificar visualización durante la navegación",
                        _options.SimplifyDuringNavigation, b => _options.SimplifyDuringNavigation = b));
                Group("Modo de gráficos", p => Check(p, "Suavizar líneas con anti-aliasing",
                    _options.AntiAliasing, b => _options.AntiAliasing = b));
                Note(_page, "Los ajustes de calidad pendientes de conectar al renderizador se guardan, pero todavía no modifican la canalización gráfica.");
                break;
            case 4:
                Group("Configuración de hardware", p =>
                {
                    Note(p, "Equipo: " + Environment.MachineName);
                    Note(p, "Sistema: " + Environment.OSVersion);
                    Check(p, "Utilizar aceleración de hardware (preferencia)",
                        _options.EnableHardwareAcceleration, b => _options.EnableHardwareAcceleration = b);
                    Note(p, "La detección detallada de GPU y controladores requiere un módulo de diagnóstico.");
                });
                break;
            case 5:
                Group("Ubicaciones de archivos", p =>
                {
                    FolderField(p, "Carpeta por defecto para proyectos", _options.DefaultProjectFolder,
                        s => _options.DefaultProjectFolder = s);
                    FolderField(p, "Carpeta por defecto para familias", _options.DefaultFamilyFolder,
                        s => _options.DefaultFamilyFolder = s);
                    Note(p, "Estos directorios se guardan como preferencias; su uso por todos los diálogos de archivo se incorporará progresivamente.");
                });
                break;
            case 6:
                Note(_page, "Renderización");
                Note(_page, "La configuración principal de calidad y luz se gestiona actualmente desde el panel Render del proyecto.");
                break;
            case 7:
                Group("Diccionario", p => Choice(p, "Idioma principal", _options.DictionaryLanguage,
                    ["Español", "Inglés"], s => _options.DictionaryLanguage = s));
                Group("Revisión ortográfica", p =>
                    Check(p, "Revisar ortografía mientras se escribe (preferencia)",
                        _options.CheckSpelling, b => _options.CheckSpelling = b));
                break;
            case 8:
                Group("Visibilidad", p => Check(p, "Mostrar información de herramientas de navegación",
                    _options.ShowNavigationWheelText, b => _options.ShowNavigationWheelText = b));
                Group("Herramienta Paseo", p =>
                    Choice(p, "Velocidad", _options.WalkSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["1", "2", "3", "5", "10"], s => _options.WalkSpeed = double.Parse(s,
                            System.Globalization.CultureInfo.InvariantCulture)));
                Group("Historial", p => Check(p, "Guardar instantáneas de la vista actual",
                    _options.SaveNavigationHistory, b => _options.SaveNavigationHistory = b));
                break;
            case 9:
                Group("Apariencia de ViewCube", p =>
                {
                    Check(p, "Mostrar ViewCube", _options.ShowViewCube, b => _options.ShowViewCube = b);
                    Choice(p, "Posición en pantalla", _options.ViewCubePosition,
                        ["Superior derecha", "Superior izquierda", "Inferior derecha", "Inferior izquierda"],
                        s => _options.ViewCubePosition = s);
                    Choice(p, "Opacidad si inactivo", _options.ViewCubeOpacity.ToString(),
                        ["25", "50", "75", "100"], s => _options.ViewCubeOpacity = int.Parse(s), " %");
                });
                Group("Al hacer clic en ViewCube", p =>
                {
                    Check(p, "Usar transición animada", _options.AnimateViewCube, b => _options.AnimateViewCube = b);
                    Check(p, "Mantener verticalidad de la escena", _options.KeepSceneVertical, b => _options.KeepSceneVertical = b);
                });
                break;
            case 10:
                Note(_page, "Macros");
                Note(_page, "El entorno de macros todavía no está disponible. No se ejecutarán scripts sin un motor de macros y permisos definidos.");
                break;
            case 11:
                Note(_page, "Modelo en la nube");
                Note(_page, "La colaboración en la nube requiere autenticación, almacenamiento y sincronización de modelos. No está habilitada en esta versión.");
                break;
        }
    }

    private void Group(string title, Action<StackPanel> build)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 8, 8, 8) };
        build(panel);
        var group = new GroupBox { Header = title, Content = panel, Margin = new Thickness(0, 0, 0, 10),
            BorderBrush = Brushes.LightGray, Padding = new Thickness(4) };
        _page.Children.Add(group);
    }

    private static void Note(Panel panel, string value) => panel.Children.Add(new TextBlock
    {
        Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 5, 2, 9)
    });

    private static void Check(Panel parent, string title, bool value, Action<bool> change)
    {
        var checkbox = new CheckBox { Content = title, IsChecked = value, Margin = new Thickness(2, 7, 2, 7) };
        checkbox.Checked += (_, _) => change(true);
        checkbox.Unchecked += (_, _) => change(false);
        parent.Children.Add(checkbox);
    }

    private static void Choice(Panel parent, string title, string selected,
        string[] choices, Action<string> change, string suffix = "")
    {
        var row = new DockPanel { Margin = new Thickness(2, 6, 2, 6) };
        var picker = new ComboBox { Width = 174, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var option in choices) picker.Items.Add(option + suffix);
        picker.SelectedItem = selected + suffix;
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is string item) change(item[..^suffix.Length]);
        };
        DockPanel.SetDock(picker, Dock.Right);
        row.Children.Add(picker);
        row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) });
        parent.Children.Add(row);
    }

    private static void ColorField(Panel parent, string title, string initial, Action<string> update)
    {
        var row = new DockPanel { Margin = new Thickness(2, 5, 2, 5) };
        var input = new TextBox { Width = 132, Text = initial, HorizontalContentAlignment = HorizontalAlignment.Center };
        input.TextChanged += (_, _) =>
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(input.Text);
                if (color.A == 255) { input.ClearValue(Control.BorderBrushProperty); update(input.Text); }
            }
            catch (FormatException) { input.BorderBrush = Brushes.OrangeRed; }
        };
        DockPanel.SetDock(input, Dock.Right);
        row.Children.Add(input);
        row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        parent.Children.Add(row);
    }

    private void FolderField(Panel parent, string title, string current, Action<string> update)
    {
        Note(parent, title);
        var row = new DockPanel();
        var browse = new Button { Content = "Examinar...", Width = 90 };
        var input = new TextBox { Text = current, MinWidth = 200, VerticalContentAlignment = VerticalAlignment.Center };
        input.TextChanged += (_, _) => update(input.Text);
        browse.Click += (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = title };
            if (picker.ShowDialog(this) == true) input.Text = picker.FolderName;
        };
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(input);
        parent.Children.Add(row);
    }
}

/// <summary>Small shortcut editor; data is saved only on accepting its parent Options dialog.</summary>
internal sealed class ShortcutOptionsWindow : Window
{
    public ShortcutOptionsWindow(Window owner, Dictionary<string, string> shortcuts)
    {
        Owner = owner;
        Title = "Teclas de acceso rápido";
        Width = 640;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(14) };
        var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true };
        grid.Columns.Add(new DataGridTextColumn { Header = "Comando", Binding = new System.Windows.Data.Binding("Key"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Teclas asignadas", Binding = new System.Windows.Data.Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.ItemsSource = shortcuts.ToArray();
        var editor = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        var command = new ComboBox { Width = 210, ItemsSource = new[] { "Nuevo", "Abrir", "Guardar", "Guardar como", "Deshacer", "Rehacer" }, SelectedIndex = 0 };
        var keys = new TextBox { Width = 160, Margin = new Thickness(8, 0, 8, 0), ToolTip = "Ejemplo: Ctrl+Shift+S" };
        var assign = new Button { Content = "Asignar" };
        assign.Click += (_, _) =>
        {
            var value = keys.Text.Trim();
            if (value.Length == 0) return;
            if (shortcuts.Any(kv => kv.Key != (string)command.SelectedItem && kv.Value.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "Esa combinación ya está asignada.", "Conflicto");
                return;
            }
            shortcuts[(string)command.SelectedItem] = value;
            grid.ItemsSource = shortcuts.ToArray();
        };
        editor.Children.Add(command); editor.Children.Add(keys); editor.Children.Add(assign);
        DockPanel.SetDock(editor, Dock.Bottom);
        panel.Children.Add(editor);
        var finish = new Button { Content = "Cerrar", Width = 96, HorizontalAlignment = HorizontalAlignment.Right };
        finish.Click += (_, _) => Close();
        DockPanel.SetDock(finish, Dock.Bottom);
        panel.Children.Add(finish);
        panel.Children.Add(grid);
        Content = panel;
    }
}
