using System.Globalization;
using System.Text.Json;

namespace DigitalBrain.UI;

internal static class TablePolicy
{
    private static readonly JsonElement Null = JsonSerializer.SerializeToElement<object?>(null);

    internal static TableSnapshot Create(string id, CreateTable input)
    {
        Require(input is not null, "Table is required.");
        Require(!string.IsNullOrWhiteSpace(input!.Title) && input.Title.Length <= 200, "Title must contain 1–200 characters.");
        Require(input.Columns is { Count: > 0 and <= 32 }, "Provide 1–32 columns.");
        Require(input.Rows is { Count: <= 1000 }, "Provide at most 1,000 rows.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in input.Columns!)
        {
            Require(column is not null && ValidId(column.Id) && ids.Add(column.Id), "Column IDs must be unique nonblank strings of at most 100 characters.");
            Require(!string.IsNullOrWhiteSpace(column!.Label) && column.Label.Length <= 200, "Column labels must contain 1–200 characters.");
            Require(column.Type is "text" or "number" or "date" or "boolean", "Column type must be text, number, date or boolean.");
        }
        ids.Clear();
        foreach (var row in input.Rows!)
        {
            Require(row is not null && ValidId(row.Id) && ids.Add(row.Id), "Row IDs must be unique nonblank strings of at most 100 characters.");
            Require(row!.Cells is not null && row.Cells.Count == input.Columns.Count, "Every row must contain one cell per column.");
            for (var index = 0; index < input.Columns.Count; index++) { ValidateValue(input.Columns[index].Type, row.Cells![index], true); }
        }
        return new(id, input.Title.Trim(), 1, input.Columns.ToArray(),
            input.Rows.Select(row => new TableRow(row.Id, row.Cells.Select(value => value.Clone()).ToArray())).ToArray(),
            [], null, input.Columns.Select(column => column.Id).ToArray(), input.Rows.Count, input.Rows.Count, 0, 50);
    }

    internal static UpdateTableView ValidateView(TableSnapshot source, UpdateTableView input)
    {
        Require(input is not null, "View is required.");
        Require(input!.ExpectedRevision > 0, "expectedRevision must be positive.");
        Require(input.Filters is { Count: <= 64 }, "Provide at most 64 filters.");
        Require(input.VisibleColumns is { Count: > 0 and <= 32 }, "At least one column must be visible.");
        var columns = source.Columns.ToDictionary(column => column.Id, StringComparer.Ordinal);
        Require(input.VisibleColumns!.All(id => id is not null && columns.ContainsKey(id))
            && input.VisibleColumns.Distinct(StringComparer.Ordinal).Count() == input.VisibleColumns.Count,
            "Visible columns must be unique existing column IDs.");
        Require(input.Sort is null || (input.Sort.ColumnId is not null && columns.ContainsKey(input.Sort.ColumnId)), "Sort column does not exist.");
        foreach (var filter in input.Filters!)
        {
            Require(filter is not null && filter.ColumnId is not null && columns.ContainsKey(filter.ColumnId), "Filter column does not exist.");
            var type = columns[filter!.ColumnId!].Type;
            Require(filter.Operator is "eq" or "neq" or "contains" or "gt" or "gte" or "lt" or "lte" or "isNull" or "isNotNull", "Unknown filter operator.");
            if (filter.Operator is "isNull" or "isNotNull")
            {
                Require(filter.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null, "Null checks take no value.");
                continue;
            }
            Require(filter.Operator != "contains" || type == "text", "contains is only supported for text.");
            Require(filter.Operator is not ("gt" or "gte" or "lt" or "lte") || type is "number" or "date", "Ordered comparisons require a number or date column.");
            ValidateValue(type, filter.Value, filter.Operator is "eq" or "neq");
        }
        return Normalize(input);
    }

    // A filter posted without a value carries an undefined JsonElement, which cannot be serialised
    // into a command; it means null, so it becomes one before the view travels.
    internal static UpdateTableView Normalize(UpdateTableView input)
        => input with
        {
            Filters = (input.Filters ?? []).Select(filter => filter with { Value = filter.Value.ValueKind == JsonValueKind.Undefined ? Null : filter.Value.Clone() }).ToArray(),
            VisibleColumns = (input.VisibleColumns ?? []).ToArray(),
        };

    internal static void ValidatePage(int offset, int limit)
        => Require(offset >= 0 && limit is > 0 and <= 200, "Offset must be nonnegative and limit must be between 1 and 200.");

    internal static TableSnapshot Query(TableSnapshot source, int offset, int limit)
    {
        ValidatePage(offset, limit);
        var filtered = Apply(source);
        return source with { Rows = filtered.Skip(offset).Take(limit).ToArray(), FilteredRows = filtered.Count, Offset = offset, Limit = limit };
    }

    // The single definition of filter and sort semantics; other row sources reuse it for parity.
    internal static IReadOnlyList<TableRow> Apply(TableSnapshot source)
    {
        var columns = source.Columns.Select((column, index) => (column, index)).ToDictionary(item => item.column.Id, StringComparer.Ordinal);
        IEnumerable<TableRow> rows = source.Rows;
        foreach (var filter in source.Filters)
        {
            var (column, index) = columns[filter.ColumnId];
            rows = rows.Where(row => Matches(column.Type, row.Cells[index], filter));
        }
        if (source.Sort is { } sort)
        {
            var (column, index) = columns[sort.ColumnId];
            var comparer = Comparer<JsonElement>.Create((left, right) => Compare(column.Type, left, right));
            rows = sort.Descending ? rows.OrderByDescending(row => row.Cells[index], comparer) : rows.OrderBy(row => row.Cells[index], comparer);
        }
        return rows.ToArray();
    }

    private static bool Matches(string type, JsonElement cell, TableFilter filter)
    {
        var isNull = cell.ValueKind == JsonValueKind.Null;
        if (filter.Operator == "isNull") { return isNull; }
        if (filter.Operator == "isNotNull") { return !isNull; }
        if (filter.Operator == "contains") { return !isNull && cell.GetString()!.Contains(filter.Value.GetString()!, StringComparison.OrdinalIgnoreCase); }
        if (filter.Operator is not ("eq" or "neq") && isNull) { return false; }
        var comparison = Compare(type, cell, filter.Value);
        return filter.Operator switch
        {
            "eq" => comparison == 0,
            "neq" => comparison != 0,
            "gt" => comparison > 0,
            "gte" => comparison >= 0,
            "lt" => comparison < 0,
            "lte" => comparison <= 0,
            _ => false,
        };
    }

    private static int Compare(string type, JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Null) { return right.ValueKind == JsonValueKind.Null ? 0 : -1; }
        if (right.ValueKind == JsonValueKind.Null) { return 1; }
        return type switch
        {
            "number" => left.GetDecimal().CompareTo(right.GetDecimal()),
            "boolean" => left.GetBoolean().CompareTo(right.GetBoolean()),
            _ => StringComparer.OrdinalIgnoreCase.Compare(left.GetString(), right.GetString()),
        };
    }

    private static void ValidateValue(string type, JsonElement value, bool allowNull)
    {
        if (allowNull && value.ValueKind == JsonValueKind.Null) { return; }
        var valid = type switch
        {
            "text" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 4000,
            "number" => ValidNumber(value),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "date" => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => false,
        };
        Require(valid, type == "number"
            ? "Numbers must have at most 15 significant decimal digits, absolute value at most 9007199254740991, and round-trip exactly through decimal without underflow (up to 28 decimal places)."
            : $"Value must match column type '{type}' (dates use yyyy-MM-dd, text up to 4,000 characters).");
    }

    private static bool ValidNumber(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)
            || Math.Abs(number) > 9007199254740991m || !value.TryGetDouble(out var clientNumber)
            || !double.IsFinite(clientNumber))
        {
            return false;
        }
        var raw = value.GetRawText();
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var digits = mantissa.TrimStart('-').Replace(".", "", StringComparison.Ordinal).Trim('0');
        // Significant mantissa digits constrain both stored cells and future filter operands.
        // A nonzero tiny literal can parse as decimal zero; reject that silent loss explicitly.
        return digits.Length <= 15 && (digits.Length == 0 || number != 0)
            && clientNumber == (double)number;
    }

    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 100;
    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new TableValidationException(message); }
    }
}
