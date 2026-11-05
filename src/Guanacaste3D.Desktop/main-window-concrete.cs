using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    private Window? _settingsWindow;
    private Window? _shopWindow;

    /// <summary>Routes the Concrete tab: bars, fabric, covers, precast panels, settings and the reports for the workshop and the machine.</summary>
    private bool TryExecuteConcreteCommand(string commandId)
    {
        switch (commandId)
        {
            case "Rebar": RunReinforcement("Rebar", "Prompt.Rebar", "3 0.012 bottom", ApplyRebar); return true;
            case "RebarPath": RunReinforcement("RebarPath", "Prompt.RebarPath", "0 0; 0.3 0; 0.3 0.4; 0 0.4 | 0.01 1", ApplyRebarPath); return true;
            case "RebarArea": RunReinforcement("RebarArea", "Prompt.RebarArea", "0 0 5 4 0.2 0.012 x 0.05", ApplyRebarArea); return true;
            case "FabricSheet": BeginKindTool(StructuralElementKind.FabricSheet); return true;
            case "TransformedFabric": RunReinforcement("TransformedFabric", "Prompt.TransformedFabric", "0 0 30 6 2.4", ApplyTransformedFabric); return true;
            case "CustomFabricSheet": RunReinforcement("CustomFabricSheet", "Prompt.CustomFabricSheet", "0 0 3 1.5 0.15 0.006", ApplyCustomFabric); return true;
            case "FabricArea": RunReinforcement("FabricArea", "Prompt.FabricArea", "0 0 8 5", ApplyFabricArea); return true;
            case "Cover": RunReinforcement("Cover", "Prompt.Cover", "0.04", ApplyCover); return true;
            case "CoverSettings": ShowCoverSettings(); return true;
            case "ReinforcementSettings": ShowReinforcementSettings(); return true;
            case "PrecastConfiguration": ShowPrecastSettings(); return true;
            case "CesConfiguration": ShowConcreteSettings(); return true;
            case "AssemblyParts": DivideSelectedWall(); return true;
            case "PrecastReinforcement": ReinforceSelectedPanels(); return true;
            case "ShopDrawings": ShowShopReport(); return true;
            case "CamExport": ExportCutListFromDialog(); return true;
            default: return false;
        }
    }

    private bool RunReinforcement(string titleKey, string promptKey, string suggestion, Action<string> apply)
    {
        var text = TextPrompt.Ask(this, GetText("Concrete." + titleKey), GetText(promptKey), GetText("Prompt.Accept"), GetText("Prompt.Cancel"), suggestion);
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            apply(text.Trim());
            StatusText.Text = GetText("Status.ReinforcementDone");
            RefreshAll();
            return true;
        }
        catch (FormatException)
        {
            StatusText.Text = GetText("Status.MemberValuesInvalid");
            return false;
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = LocalizedErrors.Translate(exception.Message);
            return false;
        }
    }

    private static string[] Words(string text) => text.Split([(char)32, ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private double M(string[] words, int index) =>
        index < words.Length && Units.TryParseLength(words[index], out var meters) ? meters : throw new FormatException();

    private static double N(string[] words, int index) =>
        index < words.Length && double.TryParse(words[index].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : throw new FormatException();

    private void ApplyRebar(string text)
    {
        var words = Words(text);
        var host = _selectedStructuralId ?? throw new InvalidDataException("The element to reinforce does not exist.");
        var top = words.Length > 2 && (words[2].Equals("top", StringComparison.OrdinalIgnoreCase) || words[2].StartsWith("sup", StringComparison.OrdinalIgnoreCase) || words[2].StartsWith("arr", StringComparison.OrdinalIgnoreCase));
        _session.AddMemberRebar(host, (int)N(words, 0), M(words, 1), top);
    }

    private void ApplyRebarPath(string text)
    {
        var halves = text.Split('|');
        var tail = Words(halves.Length > 1 ? halves[1] : "0.012 1");
        var points = new List<Point2>();
        foreach (var part in halves[0].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split((char)32, StringSplitOptions.RemoveEmptyEntries);
            if (pair.Length != 2) throw new FormatException();
            points.Add(new Point2(M(pair, 0), M(pair, 1)));
        }
        _session.AddRebarPath(_activeLevelId, points, M(tail, 0), M(tail, 1));
    }

    private void ApplyRebarArea(string text)
    {
        var words = Words(text);
        var alongX = words.Length > 6 && words[6].Equals("x", StringComparison.OrdinalIgnoreCase);
        _session.AddRebarArea(_activeLevelId, new Point2(M(words, 0), M(words, 1)), new Point2(M(words, 2), M(words, 3)), M(words, 4), M(words, 5), alongX, words.Length > 7 ? M(words, 7) : 0.05);
    }

    private void ApplyTransformedFabric(string text)
    {
        var words = Words(text);
        var origin = new Point2(M(words, 0), M(words, 1));
        var angle = N(words, 2) * Math.PI / 180;
        var length = M(words, 3);
        _session.AddFabricSheet(_activeLevelId, origin, new Point2(origin.X + Math.Cos(angle) * length, origin.Y + Math.Sin(angle) * length), M(words, 4), new WireSpec(0.0055, 0.15));
    }

    private void ApplyCustomFabric(string text)
    {
        var words = Words(text);
        var origin = new Point2(M(words, 0), M(words, 1));
        var length = M(words, 2);
        _session.AddFabricSheet(_activeLevelId, origin, new Point2(origin.X + length, origin.Y), M(words, 3), new WireSpec(M(words, 5), M(words, 4)));
    }

    private void ApplyFabricArea(string text)
    {
        var words = Words(text);
        var wires = words.Length > 5 ? new WireSpec(M(words, 5), M(words, 4)) : new WireSpec(0.0055, 0.15);
        _session.AddFabricArea(_activeLevelId, new Point2(M(words, 0), M(words, 1)), new Point2(M(words, 2), M(words, 3)), wires);
    }

    private void ApplyCover(string text)
    {
        var target = _selectedStructuralId ?? _selectedWallId ?? _selectedColumnId ?? _selectedFloorId ?? throw new InvalidDataException("The element does not exist.");
        var clear = text.Equals("0", StringComparison.Ordinal) || text.StartsWith("quitar", StringComparison.CurrentCultureIgnoreCase);
        _session.SetCover(target, clear ? null : M(Words(text), 0));
    }

    // ------------------------------------------------------------------ precast panels and reports

    private void DivideSelectedWall()
    {
        if (_selectedWallId is not { } wall)
        {
            StatusText.Text = GetText("Status.WallProfileNone");
            return;
        }
        try
        {
            var panels = _session.DivideWallIntoPanels(wall);
            StatusText.Text = FormatText("Status.PanelsMade", panels.Count);
            RefreshAll();
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }

    private void ReinforceSelectedPanels()
    {
        if (_selectedWallId is not { } wall)
        {
            StatusText.Text = GetText("Status.WallProfileNone");
            return;
        }
        // A wall that is part of an assembly of panels takes its sisters with it.
        var ids = _session.Document.ElementGroups.FirstOrDefault(group => group.Kind == ElementGroupKind.Assembly && group.MemberIds.Contains(wall))?.MemberIds.ToList() ?? [wall];
        try
        {
            var bars = _session.AddPanelReinforcement(ids);
            StatusText.Text = FormatText("Status.PanelBarsAdded", bars.Count, ids.Count);
            RefreshAll();
        }
        catch (InvalidDataException exception) { StatusText.Text = LocalizedErrors.Translate(exception.Message); }
    }

    /// <summary>The workshop listing: the precast assemblies with the size of each part, then the bars and fabric of the project.</summary>
    internal string ShopReportText()
    {
        var document = _session.Document;
        var lines = new List<string>();
        foreach (var assembly in document.ElementGroups.Where(group => group.Kind == ElementGroupKind.Assembly))
        {
            lines.Add($"■ {assembly.Name}");
            foreach (var wall in document.Walls.Where(item => assembly.MemberIds.Contains(item.Id)))
                lines.Add("   " + FormatText("Shop.Part", wall.Id.ToString("N")[..6], Units.FormatLength(wall.Length), Units.FormatLength(wall.Thickness), Units.FormatLength(wall.Height), Units.FormatVolume(wall.Length * wall.Thickness * wall.Height)));
        }
        var report = ReinforcementReport.Build(document);
        lines.Add(string.Empty);
        lines.Add(GetText("Shop.Bars"));
        if (report.Bars.Count == 0) lines.Add("   " + GetText("Shop.NoBars"));
        for (var index = 0; index < report.Bars.Count; index++)
        {
            var mark = report.Bars[index];
            lines.Add("   " + FormatText("Shop.Mark", index + 1, mark.Diameter * 1000, Units.FormatLength(mark.Length), mark.Count, mark.Weight(report.Density)));
        }
        lines.Add(FormatText("Shop.Fabric", Units.FormatArea(report.FabricArea), report.FabricWeight));
        lines.Add(FormatText("Shop.Total", report.TotalWeight, Units.FormatVolume(report.ConcreteVolume)));
        return string.Join(Environment.NewLine, lines);
    }

    private void ShowShopReport()
    {
        _shopWindow?.Close();
        var window = NewManageWindow("Shop.Title", 560);
        window.Closed += (_, _) => _shopWindow = null;
        _shopWindow = window;
        var text = new TextBox { Name = "ShopText", Text = ShopReportText(), IsReadOnly = true, Height = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 0, 6) };
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("ShopSave", GetText("Shop.Save"), (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = _session.Document.Name + "-armadura.csv" };
            if (dialog.ShowDialog(this) == true) SaveShopCsv(dialog.FileName);
        }));
        buttons.Children.Add(ManageButton("ShopClose", GetText("Phases.Close"), (_, _) => window.Close()));
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText("Shop.Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(text);
        layout.Children.Add(buttons);
        window.Content = layout;
        window.Show();
    }

    private bool SaveShopCsv(string path)
    {
        try
        {
            File.WriteAllText(path, ReinforcementReport.Build(_session.Document).ToCsv(), new UTF8Encoding(true));
            StatusText.Text = FormatText("Status.ShopSaved", Path.GetFileName(path));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Fail(exception); }
    }

    private void ExportCutListFromDialog()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = GetText("Cam.Filter"), FileName = _session.Document.Name + "-corte.bvbs" };
        if (dialog.ShowDialog(this) == true) ExportCutList(dialog.FileName);
    }

    /// <summary>Writes the cutting list for the bars of the project, one line for each mark.</summary>
    private bool ExportCutList(string path)
    {
        try
        {
            var report = ReinforcementReport.Build(_session.Document);
            if (report.Bars.Count == 0)
            {
                StatusText.Text = GetText("Status.NoBarsToExport");
                return false;
            }
            File.WriteAllText(path, report.ToCutList(_session.Document.Name, _session.Document.Reinforcement.SteelGrade), new UTF8Encoding(false));
            StatusText.Text = FormatText("Status.CutListSaved", report.Bars.Count, Path.GetFileName(path));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Fail(exception); }
    }

    // ------------------------------------------------------------------ settings forms

    private void ShowSettingsForm(string titleKey, string hintKey, IReadOnlyList<(string Name, string Label, string Value)> fields, Func<IReadOnlyDictionary<string, string>, string?> apply)
    {
        _settingsWindow?.Close();
        var window = NewManageWindow(titleKey, 440);
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        var boxes = new Dictionary<string, TextBox>();
        var layout = new StackPanel { Margin = new Thickness(14) };
        layout.Children.Add(new TextBlock { Text = GetText(hintKey), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var (name, label, value) in fields)
        {
            layout.Children.Add(new TextBlock { Text = GetText(label) });
            var box = new TextBox { Name = "Field_" + name, Text = value, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(4, 2, 4, 2) };
            boxes[name] = box;
            layout.Children.Add(box);
        }
        var message = new TextBlock { Name = "SettingsMessage", Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MinHeight = 18 };
        var buttons = new WrapPanel();
        buttons.Children.Add(ManageButton("SettingsApply", GetText("Units.Apply"), (_, _) =>
        {
            var error = apply(boxes.ToDictionary(entry => entry.Key, entry => entry.Value.Text.Trim()));
            message.Text = error ?? string.Empty;
            if (error is null) { StatusText.Text = GetText("Status.SettingsSaved"); RefreshAll(); }
        }));
        buttons.Children.Add(ManageButton("SettingsClose", GetText("Phases.Close"), (_, _) => window.Close()));
        layout.Children.Add(message);
        layout.Children.Add(buttons);
        window.Content = layout;
        window.Show();
    }

    private string? ApplySetting(Action change)
    {
        try { change(); return null; }
        catch (FormatException) { return GetText("Status.MemberValuesInvalid"); }
        catch (InvalidDataException exception) { return LocalizedErrors.Translate(exception.Message); }
    }

    private string Meters(double value) => Units.LengthNumber(value);

    private void ShowCoverSettings()
    {
        var settings = _session.Document.Reinforcement;
        ShowSettingsForm("Concrete.CoverSettings", "Concrete.CoverHint",
        [
            ("Beam", "Field.BeamCover", Meters(settings.BeamCover)), ("Column", "Field.ColumnCover", Meters(settings.ColumnCover)),
            ("Slab", "Field.SlabCover", Meters(settings.SlabCover)), ("Foundation", "Field.FoundationCover", Meters(settings.FoundationCover))
        ], values => ApplySetting(() =>
        {
            double Read(string key) => Units.TryParseLength(values[key], out var meters) ? meters : throw new FormatException();
            _session.SetReinforcementSettings(_session.Document.Reinforcement with { BeamCover = Read("Beam"), ColumnCover = Read("Column"), SlabCover = Read("Slab"), FoundationCover = Read("Foundation") });
        }));
    }

    private void ShowReinforcementSettings()
    {
        var settings = _session.Document.Reinforcement;
        ShowSettingsForm("Concrete.ReinforcementSettings", "Concrete.ReinforcementHint",
        [
            ("Diameter", "Field.DefaultDiameter", Meters(settings.DefaultDiameter)), ("Density", "Field.SteelDensity", settings.SteelDensity.ToString("0", CultureInfo.InvariantCulture)),
            ("Grade", "Field.SteelGrade", settings.SteelGrade)
        ], values => ApplySetting(() =>
            _session.SetReinforcementSettings(_session.Document.Reinforcement with
            {
                DefaultDiameter = Units.TryParseLength(values["Diameter"], out var diameter) ? diameter : throw new FormatException(),
                SteelDensity = double.TryParse(values["Density"].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var density) ? density : throw new FormatException(),
                SteelGrade = values["Grade"]
            })));
    }

    private void ShowPrecastSettings()
    {
        var settings = _session.Document.Precast;
        ShowSettingsForm("Concrete.PrecastConfiguration", "Concrete.PrecastHint",
        [
            ("Width", "Field.MaxPanelWidth", Meters(settings.MaxPanelWidth)), ("Prefix", "Field.PanelPrefix", settings.Prefix), ("Bar", "Field.PanelBar", Meters(settings.PanelBarDiameter))
        ], values => ApplySetting(() =>
            _session.SetPrecastSettings(new PrecastSettings(
                Units.TryParseLength(values["Width"], out var width) ? width : throw new FormatException(), values["Prefix"],
                Units.TryParseLength(values["Bar"], out var bar) ? bar : throw new FormatException()))));
    }

    private void ShowConcreteSettings()
    {
        var settings = _session.Document.Concrete;
        ShowSettingsForm("Concrete.CesConfiguration", "Concrete.CesHint",
        [
            ("Grade", "Field.ConcreteGrade", settings.Grade), ("Weight", "Field.UnitWeight", settings.UnitWeight.ToString("0", CultureInfo.InvariantCulture)),
            ("Steel", "Field.SteelPerM3", settings.SteelKgPerM3.ToString("0", CultureInfo.InvariantCulture))
        ], values => ApplySetting(() =>
            _session.SetConcreteSettings(new ConcreteSettings(values["Grade"],
                double.TryParse(values["Weight"].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) ? weight : throw new FormatException(),
                double.TryParse(values["Steel"].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var steel) ? steel : throw new FormatException()))));
    }
}
