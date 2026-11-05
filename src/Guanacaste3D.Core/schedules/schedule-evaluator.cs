using System.Globalization;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>Role of a row in an evaluated schedule.</summary>
public enum ScheduleRowKind
{
    Element,
    GroupHeader,
    GroupFooter,
    GrandTotal
}

/// <summary>One evaluated row: display cells, the number of elements it represents, and its nesting depth.</summary>
public sealed record ScheduleTableRow(ScheduleRowKind Kind, IReadOnlyList<string> Cells, int Count, int Depth,
    IReadOnlyList<Guid> ElementIds);

/// <summary>A schedule evaluated against the current document; the last column is "Count" when not itemized.</summary>
public sealed record ScheduleTable(ScheduleDefinition Definition, IReadOnlyList<CategoryScheduleColumn> Columns,
    IReadOnlyList<ScheduleTableRow> Rows, int ElementCount, bool HasCountColumn);

/// <summary>
/// Evaluates saved schedules: filters, multi-level sorting, group header and
/// footer rows, merging of identical rows when not itemized, and totals.
/// Lengths, areas, volumes, currency, and plain numbers are summed in footers
/// and the grand total; heights, widths, and elevations are not, since their
/// sum has no meaning.
/// </summary>
public static class ScheduleEvaluator
{
    private const string CountKey = "Count";
    private static readonly HashSet<string> SummableQuantities =
        [ScheduleQuantities.Length, ScheduleQuantities.NetVolume, ScheduleQuantities.Area];

    /// <summary>Evaluates a definition. Yes/no labels come from the caller's localization.</summary>
    public static ScheduleTable Evaluate(BuildingDocument document, ScheduleDefinition definition,
        CultureInfo culture, string yesLabel = "Yes", string noLabel = "No", ProjectUnits? units = null)
    {
        var source = CategoryScheduleService.Build(document, definition.Category);
        var byKey = source.Columns.ToDictionary(column => column.Key, StringComparer.Ordinal);
        var columns = definition.Fields.Where(byKey.ContainsKey).Select(key => byKey[key]).ToList();
        var rows = source.Rows.Where(row => definition.Filters.All(filter =>
            byKey.TryGetValue(filter.FieldKey, out var column) && Matches(row, column, filter))).ToList();
        var sorts = definition.SortGroups.Where(sort => byKey.ContainsKey(sort.FieldKey)).ToArray();
        rows.Sort((left, right) =>
        {
            foreach (var sort in sorts)
            {
                var order = Compare(Raw(left, byKey[sort.FieldKey]), Raw(right, byKey[sort.FieldKey]), culture);
                if (order != 0) return sort.Descending ? -order : order;
            }
            return string.CompareOrdinal(left.Element.Mark, right.Element.Mark);
        });

        var output = new List<ScheduleTableRow>();
        string Cell(CategoryScheduleRow row, CategoryScheduleColumn column) =>
            CategoryScheduleService.FormatCell(row, column, culture, yesLabel, noLabel, units);
        string GroupKey(CategoryScheduleRow row, int depth) =>
            string.Join("\u001f", sorts.Take(depth + 1).Select(sort => Cell(row, byKey[sort.FieldKey])));

        void Emit(List<CategoryScheduleRow> block, int depth)
        {
            if (depth == sorts.Length)
            {
                EmitLeaves(block, depth);
                return;
            }
            if (!sorts[depth].GroupHeader && !sorts[depth].GroupFooter)
            {
                Emit(block, depth + 1);
                return;
            }
            foreach (var group in block.GroupBy(row => GroupKey(row, depth)))
            {
                var members = group.ToList();
                var label = Cell(members[0], byKey[sorts[depth].FieldKey]);
                if (sorts[depth].GroupHeader)
                    output.Add(new(ScheduleRowKind.GroupHeader, LabelRow(columns.Count, label, definition.Itemize), members.Count, depth, []));
                Emit(members, depth + 1);
                if (sorts[depth].GroupFooter)
                    output.Add(new(ScheduleRowKind.GroupFooter, TotalsRow(members, columns, label, definition.Itemize, culture, units),
                        members.Count, depth, members.Select(row => row.Element.Id).ToArray()));
            }
        }

        void EmitLeaves(List<CategoryScheduleRow> block, int depth)
        {
            if (definition.Itemize)
            {
                foreach (var row in block)
                    output.Add(new(ScheduleRowKind.Element, columns.Select(column => Cell(row, column)).ToArray(), 1, depth,
                        [row.Element.Id]));
                return;
            }
            foreach (var merged in block.GroupBy(row => string.Join("\u001f", sorts.Select(sort => Cell(row, byKey[sort.FieldKey])))))
            {
                var members = merged.ToList();
                var cells = columns.Select(column =>
                {
                    if (IsSummable(column)) return FormatSum(members, column, culture, units);
                    var values = members.Select(row => Cell(row, column)).Distinct().ToArray();
                    return values.Length == 1 ? values[0] : string.Empty;
                }).Append(members.Count.ToString(culture)).ToArray();
                output.Add(new(ScheduleRowKind.Element, cells, members.Count, depth, members.Select(row => row.Element.Id).ToArray()));
            }
        }

        Emit(rows, 0);
        if (definition.GrandTotals)
            output.Add(new(ScheduleRowKind.GrandTotal, TotalsRow(rows, columns, null, definition.Itemize, culture, units),
                rows.Count, 0, rows.Select(row => row.Element.Id).ToArray()));
        return new ScheduleTable(definition, columns, output, rows.Count, !definition.Itemize);
    }

    /// <summary>Exports an evaluated schedule as CSV with a leading row-kind column.</summary>
    public static string ToCsv(ScheduleTable table)
    {
        var csv = new StringBuilder();
        var headers = new List<string> { "Row" };
        headers.AddRange(table.Columns.Select(column =>
            CategoryScheduleService.HeaderWithUnit(column.Definition?.Name ?? column.Key, column.Unit)));
        if (table.HasCountColumn) headers.Add(CountKey);
        csv.AppendJoin(',', headers.Select(Quote)).AppendLine();
        foreach (var row in table.Rows)
            csv.AppendJoin(',', row.Cells.Prepend(row.Kind.ToString()).Select(Quote)).AppendLine();
        return csv.ToString();
    }

    /// <summary>Builds a header row whose first cell carries the group label.</summary>
    private static string[] LabelRow(int columnCount, string label, bool itemize)
    {
        var cells = Enumerable.Repeat(string.Empty, columnCount + (itemize ? 0 : 1)).ToArray();
        if (cells.Length > 0) cells[0] = label;
        return cells;
    }

    /// <summary>Builds a footer or grand-total row: label, sums of summable columns, and the element count.</summary>
    private static string[] TotalsRow(IReadOnlyList<CategoryScheduleRow> rows, IReadOnlyList<CategoryScheduleColumn> columns,
        string? label, bool itemize, CultureInfo culture, ProjectUnits? units)
    {
        var cells = columns.Select(column => IsSummable(column) ? FormatSum(rows, column, culture, units) : string.Empty).ToList();
        if (!itemize) cells.Add(rows.Count.ToString(culture));
        if (cells.Count > 0 && !IsSummable(columns[0]))
            cells[0] = label is null ? rows.Count.ToString(culture) : $"{label} ({rows.Count.ToString(culture)})";
        return [.. cells];
    }

    /// <summary>Returns whether totals of a column are meaningful.</summary>
    private static bool IsSummable(CategoryScheduleColumn column) => column.Definition is { } definition
        ? definition.Spec is ParameterSpec.Length or ParameterSpec.Area or ParameterSpec.Volume or
            ParameterSpec.Currency or ParameterSpec.Number or ParameterSpec.Integer
        : SummableQuantities.Contains(column.Key);

    /// <summary>Formats the sum of a column over rows, or empty when no row has a value.</summary>
    private static string FormatSum(IEnumerable<CategoryScheduleRow> rows, CategoryScheduleColumn column, CultureInfo culture, ProjectUnits? units = null)
    {
        var numbers = rows.Select(row => Raw(row, column).Number).OfType<double>().ToArray();
        if (numbers.Length == 0) return string.Empty;
        var sum = numbers.Sum();
        if (units is not null && column.Unit.Length > 0 && units.NumberFor(column.Unit, sum) is { } shown) return shown;
        return column.Definition?.Spec == ParameterSpec.Currency ? sum.ToString("N2", culture) : sum.ToString("0.###", culture);
    }

    /// <summary>Returns the comparable value of a cell: a number when numeric, otherwise text.</summary>
    private static (double? Number, string Text) Raw(CategoryScheduleRow row, CategoryScheduleColumn column)
    {
        if (column.Definition is { } definition)
        {
            var value = row.Values.GetValueOrDefault(definition.Id);
            return (value?.Number, value?.Text ?? (value?.Number is double n ? n.ToString(CultureInfo.InvariantCulture) : ""));
        }
        var formatted = CategoryScheduleService.FormatCell(row, column, CultureInfo.InvariantCulture);
        return row.BuiltIns.GetValueOrDefault(column.Key) is double number ? (number, formatted) : (null, formatted);
    }

    /// <summary>Orders two cells numerically when both are numbers, otherwise by culture-aware text.</summary>
    private static int Compare((double? Number, string Text) left, (double? Number, string Text) right, CultureInfo culture) =>
        left.Number is double a && right.Number is double b ? a.CompareTo(b)
            : string.Compare(left.Text, right.Text, culture, CompareOptions.IgnoreCase);

    /// <summary>Tests a filter against one row.</summary>
    private static bool Matches(CategoryScheduleRow row, CategoryScheduleColumn column, ScheduleFilter filter)
    {
        var (number, text) = Raw(row, column);
        var hasValue = number is not null || text.Length > 0;
        if (filter.Operator == ScheduleFilterOperator.HasValue) return hasValue;
        if (filter.Operator == ScheduleFilterOperator.HasNoValue) return !hasValue;
        if (!hasValue) return filter.Operator is ScheduleFilterOperator.NotEqual or ScheduleFilterOperator.NotContains;
        var spec = column.Definition?.Spec ?? (number is null ? ParameterSpec.Text : ParameterSpec.Number);
        double? target = null;
        if (number is not null && ParameterValueText.TryParse(spec, filter.Value, CultureInfo.InvariantCulture,
                out var parsed, out _)) target = parsed;
        var comparison = number is double n && target is double t
            ? (Math.Abs(n - t) <= 1e-9 * Math.Max(1, Math.Abs(t)) ? 0 : n.CompareTo(t))
            : string.Compare(text, filter.Value, StringComparison.CurrentCultureIgnoreCase);
        return filter.Operator switch
        {
            ScheduleFilterOperator.Equal => comparison == 0,
            ScheduleFilterOperator.NotEqual => comparison != 0,
            ScheduleFilterOperator.GreaterThan => comparison > 0,
            ScheduleFilterOperator.GreaterOrEqual => comparison >= 0,
            ScheduleFilterOperator.LessThan => comparison < 0,
            ScheduleFilterOperator.LessOrEqual => comparison <= 0,
            ScheduleFilterOperator.Contains => text.Contains(filter.Value, StringComparison.CurrentCultureIgnoreCase),
            ScheduleFilterOperator.NotContains => !text.Contains(filter.Value, StringComparison.CurrentCultureIgnoreCase),
            ScheduleFilterOperator.BeginsWith => text.StartsWith(filter.Value, StringComparison.CurrentCultureIgnoreCase),
            ScheduleFilterOperator.EndsWith => text.EndsWith(filter.Value, StringComparison.CurrentCultureIgnoreCase),
            _ => false
        };
    }

    /// <summary>Quotes a CSV cell and neutralizes spreadsheet formula prefixes.</summary>
    private static string Quote(string value) => SpreadsheetCsv.Quote(value, preserveNumeric: true);
}
