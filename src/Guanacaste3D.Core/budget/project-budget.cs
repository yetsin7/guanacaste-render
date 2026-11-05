using System.Globalization;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>One unit price, identified by a stable code so a price list can be edited without touching the model.</summary>
public sealed record UnitPrice(string Code, string Description, string Unit, decimal Price);

/// <summary>An editable table of unit prices in a single currency. The defaults are reference values to be replaced.</summary>
public sealed class UnitPriceList
{
    public const string WallArea = "WALL_AREA";
    public const string FloorArea = "FLOOR_AREA";
    public const string RoofArea = "ROOF_AREA";
    public const string Door = "DOOR";
    public const string Window = "WINDOW";
    public const string Column = "COLUMN";

    private readonly Dictionary<string, UnitPrice> _prices;

    public string Currency { get; }

    public UnitPriceList(string currency, IEnumerable<UnitPrice> prices)
    {
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("A currency is required.", nameof(currency));
        Currency = currency.Trim().ToUpperInvariant();
        _prices = prices.ToDictionary(price => price.Code, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<UnitPrice> Prices => _prices.Values;

    /// <summary>Returns the price for a code, or null when the list does not price that item.</summary>
    public UnitPrice? Find(string code) => _prices.GetValueOrDefault(code);

    /// <summary>Returns a copy with one price added or replaced.</summary>
    public UnitPriceList With(UnitPrice price)
    {
        if (price.Price < 0) throw new InvalidDataException("A unit price cannot be negative.");
        var copy = new Dictionary<string, UnitPrice>(_prices, StringComparer.Ordinal) { [price.Code] = price };
        return new UnitPriceList(Currency, copy.Values);
    }

    /// <summary>Reference prices in US dollars. They are placeholders: real projects must set local prices.</summary>
    public static UnitPriceList Default() => new("USD",
    [
        new(WallArea, "Wall, finished both faces", "m²", 45m),
        new(FloorArea, "Floor slab", "m²", 90m),
        new(RoofArea, "Roof slab", "m²", 70m),
        new(Door, "Door, supplied and fitted", "u", 280m),
        new(Window, "Window, supplied and fitted", "u", 220m),
        new(Column, "Structural column", "u", 350m)
    ]);
}

/// <summary>A priced quantity in a budget.</summary>
public sealed record BudgetLine(string Code, string Description, string Unit, double Quantity, decimal UnitPrice, decimal Total);

/// <summary>A budget derived from the model quantities, with contingency applied on top of the subtotal.</summary>
public sealed record ProjectBudget(string Currency, IReadOnlyList<BudgetLine> Lines, decimal Subtotal,
    decimal ContingencyRate, decimal Contingency, decimal Total)
{
    /// <summary>Exports the budget as invariant-culture CSV.</summary>
    public string ToCsv()
    {
        var csv = new StringBuilder();
        csv.AppendLine("Code,Description,Unit,Quantity,UnitPrice,Total");
        foreach (var line in Lines)
            csv.Append(line.Code).Append(',').Append('"').Append(line.Description.Replace("\"", "\"\"")).Append("\",")
                .Append(line.Unit).Append(',').Append(line.Quantity.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(line.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(line.Total.ToString("0.00", CultureInfo.InvariantCulture));
        csv.AppendLine($"SUBTOTAL,,,,,{Subtotal.ToString("0.00", CultureInfo.InvariantCulture)}");
        csv.AppendLine($"CONTINGENCY,,,{ContingencyRate.ToString("0.##", CultureInfo.InvariantCulture)},," +
            Contingency.ToString("0.00", CultureInfo.InvariantCulture));
        csv.AppendLine($"TOTAL_{Currency},,,,,{Total.ToString("0.00", CultureInfo.InvariantCulture)}");
        return csv.ToString();
    }
}

/// <summary>Prices the quantities of a validated document against a unit price list.</summary>
public static class BudgetCalculator
{
    /// <summary>Builds a budget; items the price list does not cover are skipped rather than priced at zero.</summary>
    public static ProjectBudget Build(BuildingDocument document, UnitPriceList prices, decimal contingencyRate = 0.10m)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(prices);
        if (contingencyRate is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(contingencyRate));
        document.Validate();

        var wallArea = document.Walls.Sum(wall => Math.Max(0,
            wall.ElevationArea - document.Openings.Where(o => o.WallId == wall.Id).Sum(o => o.Width * o.Height)));
        var lines = new List<BudgetLine>();
        Add(lines, prices, UnitPriceList.WallArea, wallArea);
        Add(lines, prices, UnitPriceList.FloorArea,
            document.Floors.Where(f => f.Kind == FloorKind.Floor).Sum(f => f.CalculateArea()));
        Add(lines, prices, UnitPriceList.RoofArea,
            document.Floors.Where(f => f.Kind == FloorKind.Roof).Sum(RoofGeometry.SurfaceArea));
        Add(lines, prices, UnitPriceList.Door, document.Openings.Count(o => o.Kind == OpeningKind.Door));
        Add(lines, prices, UnitPriceList.Window, document.Openings.Count(o => o.Kind == OpeningKind.Window));
        Add(lines, prices, UnitPriceList.Column, document.Columns.Count);

        var subtotal = lines.Sum(line => line.Total);
        var contingency = Math.Round(subtotal * contingencyRate, 2);
        return new ProjectBudget(prices.Currency, lines, subtotal, contingencyRate, contingency, subtotal + contingency);
    }

    private static void Add(List<BudgetLine> lines, UnitPriceList prices, string code, double quantity)
    {
        var price = prices.Find(code);
        if (price is null || quantity <= 0) return;
        var rounded = Math.Round(quantity, 3);
        lines.Add(new BudgetLine(code, price.Description, price.Unit, rounded, price.Price,
            Math.Round((decimal)rounded * price.Price, 2)));
    }
}
