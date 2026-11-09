using System.IO;
using System.Windows;
using Microsoft.Win32;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>
    /// Exports level plans, sections, and elevations with coordinated model geometry, on the project sheet paper
    /// (A3 when there is none).
    /// </summary>
    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = GetText("Dialog.PdfFilter"),
            FileName = _session.Document.Name + ".pdf",
            AddExtension = true,
            DefaultExt = ".pdf"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var paper = _session.Document.Sheets.FirstOrDefault()?.PaperSize ?? SheetPaperSize.A3;
            System.IO.File.WriteAllBytes(dialog.FileName,
                Guanacaste3D.Core.Interop.PdfPlanExporter.ExportDocumentation(_session.Document, paper, DateTimeOffset.Now));
            StatusText.Text = GetText("Status.PdfExported");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, LocalizedErrors.Translate(ex.Message), GetText("Dialog.ExportFailed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
