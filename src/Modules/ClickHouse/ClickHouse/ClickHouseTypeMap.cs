namespace DigitalBrain.ClickHouse;

// ClickHouse column types collapse onto the ui table's four column types. Anything that cannot be
// a JSON number under TablePolicy's 2^53 / 15 significant digit rule as a class (Int128 and up)
// is text; individual out-of-range values in number columns are guarded per cell.
internal static class ClickHouseTypeMap
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Date = "date";
    public const string Boolean = "boolean";

    public static string ToTableType(string clickHouseType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clickHouseType);
        var core = Unwrap(clickHouseType);
        if (core == "Bool")
        {
            return Boolean;
        }

        if (core is "Date" or "Date32")
        {
            return Date;
        }

        return IsNumber(core) ? Number : Text;
    }

    // Nullable(LowCardinality(String)) -> String. Only wrappers are removed; Array(T) stays Array(T).
    public static string Unwrap(string clickHouseType)
    {
        var type = clickHouseType.Trim();
        while (TryStrip(type, "Nullable(", out var inner) || TryStrip(type, "LowCardinality(", out inner))
        {
            type = inner;
        }

        return type;
    }

    // Low-cardinality strings and enums hold a small set of values worth showing to the agent;
    // Array(LowCardinality(String)) is a list, not a category, so it stays out.
    public static bool IsCategorical(string clickHouseType)
    {
        var core = Unwrap(clickHouseType);
        return core.StartsWith("Enum", StringComparison.Ordinal)
            || (core == "String" && clickHouseType.Contains("LowCardinality(", StringComparison.Ordinal));
    }

    // ORDER BY is not defined for these, so they never serve as paging tiebreakers.
    public static bool IsOrderable(string clickHouseType)
    {
        var core = Unwrap(clickHouseType);
        return !(core.StartsWith("JSON", StringComparison.Ordinal)
            || core.StartsWith("Object(", StringComparison.Ordinal)
            || core.StartsWith("AggregateFunction(", StringComparison.Ordinal)
            || core.StartsWith("SimpleAggregateFunction(", StringComparison.Ordinal)
            || core.StartsWith("Nested(", StringComparison.Ordinal)
            || core.StartsWith("Dynamic", StringComparison.Ordinal)
            || core.StartsWith("Variant(", StringComparison.Ordinal));
    }

    private static bool TryStrip(string type, string wrapper, out string inner)
    {
        if (type.StartsWith(wrapper, StringComparison.Ordinal) && type.EndsWith(')'))
        {
            inner = type[wrapper.Length..^1].Trim();
            return true;
        }

        inner = type;
        return false;
    }

    private static bool IsNumber(string core)
        => core is "Int8" or "Int16" or "Int32" or "Int64" or "UInt8" or "UInt16" or "UInt32" or "UInt64" or "Float32" or "Float64"
            || core.StartsWith("Decimal", StringComparison.Ordinal);
}
