using System.IO;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Routes the Insert tab's table imports: parameter values (Extended Properties) and review marks (Markups).</summary>
    private bool TryExecuteTableImportCommand(string commandId)
    {
        switch (commandId)
        {
            case "ExtendedProperties": PickAndRun("TableImport.Filter", ImportParameterValuesFile); return true;
            case "DwfMarkups": PickAndRun("TableImport.Filter", ImportReviewMarksFile); return true;
            default: return false;
        }
    }

    /// <summary>Fills parameter values from a table with an Id column and one column for each project parameter.</summary>
    private bool ImportParameterValuesFile(string path)
    {
        try
        {
            var report = _session.ImportParameterValues(File.ReadAllText(path));
            StatusText.Text = FormatText("Status.ParameterValuesImported", report.Applied, report.Rejected, report.UnknownElements) +
                              (report.UnknownColumns.Count > 0 ? " " + FormatText("Status.ParameterColumnsUnknown", string.Join(", ", report.UnknownColumns)) : string.Empty);
            RefreshAll();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) { return Fail(exception); }
    }

    /// <summary>Brings review marks into the active level from a table of x;y;comment lines.</summary>
    private bool ImportReviewMarksFile(string path)
    {
        try
        {
            var (made, skipped) = _session.ImportReviewMarks(_activeLevelId, File.ReadAllText(path));
            StatusText.Text = FormatText("Status.ReviewMarksImported", made, skipped);
            RefreshAll();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) { return Fail(exception); }
    }
}
