using System.Globalization;
using System.Text.Json;

namespace DigitalBrain.ClickHouse.Tables;

// Validates a saved view against the table's own columns and normalises null filter values, so no
// malformed view reaches the SQL compiler. Filter operands arrive as JSON text.
internal static class ClickHouseTablePolicy
{
    private static readonly JsonElement Null = JsonSerializer.SerializeToElement<object?>(null);

    internal static UpdateClickHouseTableView ValidateView(ClickHouseTableView source, UpdateClickHouseTableView input)
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
            Require(TryElement(filter!.Value, out var value), "Filter value must be valid JSON.");
            var type = columns[filter.ColumnId!].Type;
            Require(filter.Operator is "eq" or "neq" or "contains" or "gt" or "gte" or "lt" or "lte" or "isNull" or "isNotNull", "Unknown filter operator.");
            if (filter.Operator is "isNull" or "isNotNull")
            {
                Require(value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined, "Null checks take no value.");
                continue;
            }
            Require(filter.Operator != "contains" || type == "text", "contains is only supported for text.");
            Require(filter.Operator is not ("gt" or "gte" or "lt" or "lte") || type is "number" or "date", "Ordered comparisons require a number or date column.");
            ValidateValue(type, value, filter.Operator is "eq" or "neq");
        }
        return Normalize(input);
    }

    // A filter posted without a value means null, so it becomes the JSON text "null" before the
    // view travels. Missing lists stay as they are: this step refuses them with the same message
    // it always has, rather than quietly turning "no filters given" into "clear filters".
    internal static UpdateClickHouseTableView Normalize(UpdateClickHouseTableView input)
    {
        Require(input is not null, "View is required.");
        return input! with
        {
            Filters = input.Filters?.Select(filter => filter is null ? filter : filter with { Value = string.IsNullOrWhiteSpace(filter.Value) ? "null" : filter.Value }).ToArray()!,
            VisibleColumns = input.VisibleColumns?.ToArray()!,
        };
    }

    internal static void ValidatePage(int offset, int limit)
        => Require(offset >= 0 && limit is > 0 and <= 200, "Offset must be nonnegative and limit must be between 1 and 200.");

    internal static bool TryElement(string raw, out JsonElement value)
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

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new ClickHouseTableValidationException(message); }
    }
}
