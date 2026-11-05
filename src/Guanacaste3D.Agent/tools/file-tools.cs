using System.Text;
using Guanacaste3D.Core;
using Guanacaste3D.Core.Interop;

namespace Guanacaste3D.Agent;

/// <summary>Tools that write files outside the project. They are sensitive, so the user confirms each one.</summary>
internal static class FileTools
{
    public static IEnumerable<AgentTool> Create()
    {
        yield return Export("save_project", "Saves the project as a native .g3 file.", ".g3",
            (context, path) => ProjectFile.Save(path, context.Session.Document));
        yield return Export("export_ifc", "Exports the model as an IFC4 file.", ".ifc",
            (context, path) => File.WriteAllText(path, IfcExporter.ToStep(context.Session.Document, DateTimeOffset.Now), Encoding.UTF8));
        yield return Export("export_dxf", "Exports the plans as an ASCII DXF file.", ".dxf",
            (context, path) => File.WriteAllText(path, DxfExporter.ToAscii(context.Session.Document), Encoding.UTF8));
        yield return Export("export_quantities_csv", "Exports the wall quantity takeoff as CSV.", ".csv",
            (context, path) => File.WriteAllText(path, QuantitySchedule.ToCsv(context.Session.Document), Encoding.UTF8));
        yield return Export("export_budget_csv", "Exports the priced budget as CSV.", ".csv",
            (context, path) => File.WriteAllText(path,
                BudgetCalculator.Build(context.Session.Document, context.Prices).ToCsv(), Encoding.UTF8));
        yield return Export("export_pdf_plans", "Exports the plan sheets as a PDF file.", ".pdf",
            (context, path) => File.WriteAllBytes(path, PdfPlanExporter.Export(context.Session.Document, SheetPaperSize.A3, DateTimeOffset.Now)));
    }

    private static AgentTool Export(string name, string description, string extension, Action<AgentContext, string> write) =>
        new(name, description + " Writes outside the project, so the user must confirm.",
            Schema.Object(
                ("path", Schema.Str($"Absolute destination path ending in {extension}."), true),
                ("overwrite", new System.Text.Json.Nodes.JsonObject { ["type"] = "boolean", ["description"] = "Replace an existing file. Default false." }, false)),
            ToolRisk.Sensitive, (context, args) =>
            {
                var path = args.Text("path");
                if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("The path must be absolute.");
                if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The file must end in {extension}.");
                var directory = Path.GetDirectoryName(path);
                if (directory is null || !Directory.Exists(directory)) throw new InvalidDataException("The destination folder does not exist.");
                var overwrite = args.Raw["overwrite"]?.GetValue<bool>() ?? false;
                if (File.Exists(path) && !overwrite) throw new InvalidDataException("The file exists. Ask the user before overwriting.");
                write(context, path);
                return $"Wrote {path}.";
            });
}
