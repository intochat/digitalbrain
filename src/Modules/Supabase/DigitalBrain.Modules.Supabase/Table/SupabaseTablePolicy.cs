using System.Globalization;
using System.Text.Json;
using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Supabase;

// Validation and view normalization shared by the live query table. Rows are never persisted;
// the saved view (columns, filters, sort, visible columns, revision) is compiled to SQL on read.
// Filter operands arrive as JSON text.
internal static class SupabaseTablePolicy
{
    private const int MaxColumns = 32;
    private const int MaxTitleLength = 200;
    private static readonly JsonElement Null = JsonSerializer.SerializeToElement<object?>(null);

    public static SupabaseTableSnapshot CreateView(string id, string title, IReadOnlyList<SupabaseColumn> columns)
    {
        Require(!string.IsNullOrWhiteSpace(title) && title.Length <= MaxTitleLength, $"Provide a title of 1–{MaxTitleLength} characters.");
        Require(columns.Count is > 0 and <= MaxColumns, $"The query must return 1–{MaxColumns} columns; it returns {columns.Count}.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            Require(!string.IsNullOrWhiteSpace(column.Name) && column.Name.Length <= 100 && names.Add(column.Name),
                $"Column '{column.Name}' is blank, longer than 100 characters, or repeated; alias every column with a unique name.");
            Require(column.TableType is SupabaseTypeMap.Text or SupabaseTypeMap.Number or SupabaseTypeMap.Date or SupabaseTypeMap.Boolean,
                "Column type must be text, number, date or boolean.");
        }

        var tableColumns = columns.Select(column => new SupabaseTableColumn(column.Name, column.Name, column.TableType)).ToArray();
        return new(id, title.Trim(), 1, tableColumns, [], [], null, tableColumns.Select(column => column.Id).ToArray(), 0, 0, 0, 50);
    }

    public static UpdateSupabaseTableView ValidateView(SupabaseTableSnapshot source, UpdateSupabaseTableView input)
    {
        Require(input.ExpectedRevision > 0, "expectedRevision must be positive.");
        Require(input.Filters is { Count: <= 64 }, "Provide at most 64 filters.");
        Require(input.VisibleColumns is { Count: > 0 and <= MaxColumns }, "At least one column must be visible.");
        var columns = source.Columns.ToDictionary(column => column.Id, StringComparer.Ordinal);
        Require(input.VisibleColumns!.All(id => id is not null && columns.ContainsKey(id))
            && input.VisibleColumns.Distinct(StringComparer.Ordinal).Count() == input.VisibleColumns.Count,
            "Visible columns must be unique existing column IDs.");
        Require(input.Sort is null || (input.Sort.ColumnId is not null && columns.ContainsKey(input.Sort.ColumnId)), "Sort column does not exist.");
        foreach (var filter in input.Filters!)
        {
            Require(filter is not null && filter.ColumnId is not null && columns.ContainsKey(filter.ColumnId), "Filter column does not exist.");
            Require(TryElement(filter!.Value, out var value), "Filter value must be valid JSON.");
            var type = columns[filter.ColumnId!].Type;
            Require(filter.Operator is "eq" or "neq" or "contains" or "gt" or "gte" or "lt" or "lte" or "isNull" or "isNotNull", "Unknown filter operator.");
            if (filter.Operator is "isNull" or "isNotNull")
            {
                Require(value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined, "Null checks take no value.");
                continue;
            }
            Require(filter.Operator != "contains" || type == SupabaseTypeMap.Text, "contains is only supported for text.");
            Require(filter.Operator is not ("gt" or "gte" or "lt" or "lte") || type is SupabaseTypeMap.Number or SupabaseTypeMap.Date, "Ordered comparisons require a number or date column.");
            ValidateValue(type, value, filter.Operator is "eq" or "neq");
        }
        return Normalize(input);
    }

    // A filter posted without a value means null, so it becomes the JSON text "null" before the
    // view travels. Missing lists stay as they are: this step refuses them with the same message
    // it always has, rather than quietly turning "no filters given" into "clear filters".
    public static UpdateSupabaseTableView Normalize(UpdateSupabaseTableView input)
        => input with
        {
            Filters = input.Filters?.Select(filter => filter is null ? filter : filter with { Value = string.IsNullOrWhiteSpace(filter.Value) ? "null" : filter.Value }).ToArray()!,
            VisibleColumns = input.VisibleColumns?.ToArray()!,
        };

    public static void ValidatePage(int offset, int limit)
        => Require(offset >= 0 && limit is > 0 and <= 200, "Offset must be nonnegative and limit must be between 1 and 200.");

    public static bool TryElement(string raw, out JsonElement value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = Null;
            return true;
        }

        try
        {
            value = JsonDocument.Parse(raw).RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static void ValidateValue(string type, JsonElement value, bool allowNull)
    {
        if (allowNull && value.ValueKind == JsonValueKind.Null) { return; }
        var valid = type switch
        {
            SupabaseTypeMap.Text => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 4000,
            SupabaseTypeMap.Number => ValidNumber(value),
            SupabaseTypeMap.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SupabaseTypeMap.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => false,
        };
        Require(valid, type == SupabaseTypeMap.Number
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

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new SupabaseTableValidationException(message); }
    }
}