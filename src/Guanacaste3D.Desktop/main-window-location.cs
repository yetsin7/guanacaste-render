using System.Globalization;
using Guanacaste3D.Core;

namespace Guanacaste3D.Desktop;

public partial class MainWindow
{
    /// <summary>Asks for the plot size and street name that the location plan draws.</summary>
    private void DefineLot()
    {
        var current = LocationPlan.LotFor(_session.Document);
        var size = TextPrompt.Ask(this, GetText("Location.PromptTitle"), GetText("Location.PromptSize"),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"),
            string.Create(CultureInfo.InvariantCulture, $"{current.Width:0.##} x {current.Depth:0.##}"));
        if (size is null) return;
        if (!TryParseLotSize(size, out var width, out var depth))
        {
            StatusText.Text = GetText("Location.InvalidSize");
            return;
        }
        var street = TextPrompt.Ask(this, GetText("Location.PromptTitle"), GetText("Location.PromptStreet"),
            GetText("Prompt.Accept"), GetText("Prompt.Cancel"), current.StreetName);
        if (street is null) return;
        var lot = new SiteLot(width, depth, street.Trim());
        if (!lot.IsValid)
        {
            StatusText.Text = GetText("Location.InvalidSize");
            return;
        }
        _session.SetLot(lot);
        StatusText.Text = GetText("Location.Saved");
    }

    /// <summary>Reads "width x depth" in meters, accepting a comma or a point as the decimal mark.</summary>
    internal static bool TryParseLotSize(string text, out double width, out double depth)
    {
        width = depth = 0;
        var parts = text.Split(['x', 'X', '×', '*'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && TryNumber(parts[0], out width) && TryNumber(parts[1], out depth);

        static bool TryNumber(string value, out double number) =>
            double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && number > 0;
    }
}
