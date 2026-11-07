using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Guanacaste3D.Core.Rendering;
using Microsoft.Win32;

namespace Guanacaste3D.Desktop;

/// <summary>Manual image-to-image handoff to any external AI; no provider integration or credentials.</summary>
public partial class AiRenderWindow : Window
{
    private readonly string? _projectPath;
    private readonly string _projectName;
    private readonly BitmapSource _reference;
    private AiRenderPackage? _current;

    private sealed record StyleChoice(string Name, AiRenderStyle Style);

    public AiRenderWindow(string? projectPath, string projectName, BitmapSource reference)
    {
        InitializeComponent();
        _projectPath = projectPath;
        _projectName = projectName;
        _reference = reference;
        ReferenceImage.Source = reference;

        StylePicker.ItemsSource = new[]
        {
            new StyleChoice("Fotorrealista profesional", AiRenderStyle.Photorealistic),
            new StyleChoice("Maqueta arquitectónica", AiRenderStyle.ArchitecturalModel),
            new StyleChoice("Interiores realistas", AiRenderStyle.Interior),
            new StyleChoice("Nocturno", AiRenderStyle.Night),
            new StyleChoice("Atardecer", AiRenderStyle.Sunset),
            new StyleChoice("Acuarela arquitectónica", AiRenderStyle.Watercolor),
            new StyleChoice("Conceptual", AiRenderStyle.Concept)
        };
        StylePicker.SelectedIndex = 0;
        RefreshVault();
        if (string.IsNullOrWhiteSpace(_projectPath))
            StatusText.Text = "Proyecto sin guardar: los paquetes se conservarán en el baúl local de borradores.";
    }

    private AiRenderOptions SelectedOptions() => new(
        ((StyleChoice)StylePicker.SelectedItem!).Style,
        ImproveMaterials: MaterialsCheck.IsChecked == true,
        ImproveLighting: LightingCheck.IsChecked == true,
        ImproveShadows: ShadowsCheck.IsChecked == true,
        AddTrees: TreesCheck.IsChecked == true,
        AddVegetation: VegetationCheck.IsChecked == true,
        AddFurniture: FurnitureCheck.IsChecked == true,
        AddPeople: PeopleCheck.IsChecked == true,
        ImproveSky: SkyCheck.IsChecked == true,
        PreserveArchitecture: PreserveCheck.IsChecked == true,
        AdditionalInstructions: NotesBox.Text);

    private void Prepare_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_reference));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            _current = AiRenderVault.CreateRequest(_projectPath, _projectName, SelectedOptions(), stream.ToArray());
            RefreshVault();
            StatusText.Text = "¡Listo! Prompt e imagen guardados. Copia ambos y pégalos en ChatGPT, Gemini u otra IA.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            ShowFailure(ex);
        }
    }

    private void RefreshVault()
    {
        try
        {
            var packages = AiRenderVault.ListRequests(_projectPath, _projectName);
            PackagesList.ItemsSource = packages;
            PackagesList.SelectedItem = packages.FirstOrDefault(p => p.DirectoryPath == _current?.DirectoryPath) ??
                                        packages.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowFailure(ex);
        }
    }

    private void PackagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackagesList.SelectedItem is not AiRenderPackage selected) return;
        _current = selected;
        PromptBox.Text = selected.Prompt;
        try
        {
            // A restored prompt must always be copied with the reference that generated it,
            // not with the camera's newly displayed view.
            var bytes = File.ReadAllBytes(selected.ReferencePath);
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            ReferenceImage.Source = bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException || ex is NotSupportedException)
        {
            ReferenceImage.Source = null;
            StatusText.Text = "No se pudo cargar la referencia de esa sesión: " + ex.Message;
        }
    }

    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PromptBox.Text))
        {
            StatusText.Text = "Prepara un paquete o selecciona una sesión del baúl antes de copiar el prompt.";
            return;
        }
        try
        {
            Clipboard.SetText(PromptBox.Text);
            StatusText.Text = "Prompt copiado. Pégalo en la IA de tu preferencia.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = "El portapapeles está ocupado. Prueba de nuevo.";
        }
    }

    private void CopyReference_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ReferenceImage.Source is not BitmapSource selectedReference)
            {
                StatusText.Text = "No hay referencia disponible en esta sesión.";
                return;
            }
            Clipboard.SetImage(selectedReference);
            StatusText.Text = "Referencia de la sesión copiada. Pégala junto al prompt en la IA externa.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = "El portapapeles está ocupado. Prueba de nuevo.";
        }
    }

    private void ImportResult_Click(object sender, RoutedEventArgs e)
    {
        if (PackagesList.SelectedItem is not AiRenderPackage package)
        {
            StatusText.Text = "Prepara un paquete o selecciona una sesión para asociarle el resultado.";
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "Importar imagen generada por una IA",
            Filter = "Imagen PNG/JPEG|*.png;*.jpg;*.jpeg",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var result = AiRenderVault.ImportResult(package, dialog.FileName);
            RefreshVault();
            StatusText.Text = "Resultado archivado con su prompt original: " + Path.GetFileName(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            ShowFailure(ex);
        }
    }

    private void OpenVault_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = AiRenderVault.GetVaultDirectory(_projectPath, _projectName);
            Directory.CreateDirectory(root);
            Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowFailure(ex);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowFailure(Exception ex) =>
        MessageBox.Show(this, ex.Message, "Render con IA", MessageBoxButton.OK, MessageBoxImage.Warning);
}
