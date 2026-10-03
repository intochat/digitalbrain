namespace DigitalBrain.Contracts.Data;

[GenerateSerializer, Alias("data.row-query")]
public sealed record RowQuery
{
    public const int DefaultLimit = 50;
    public const int MaxPageSize = 200;

    [Id(0)] public RowFilter[] Filters { get; init; } = [];
    [Id(1)] public RowSort[] Sort { get; init; } = [];
    [Id(2)] public int Offset { get; init; }
    [Id(3)] public int Limit { get; init; } = DefaultLimit;
    [Id(4)] public string[] GroupBy { get; init; } = [];
    [Id(5)] public RowAggregate[] Aggregates { get; init; } = [];
    [Id(6)] public string[] Columns { get; init; } = [];

    public void Check()
    {
        if (Offset < 0) { throw new ArgumentOutOfRangeException(nameof(Offset)); }
        if (Limit is < 1 or > MaxPageSize) { throw new ArgumentOutOfRangeException(nameof(Limit)); }
    }

    public void Check(RowSchema schema)
    {
        Check();
        ArgumentNullException.ThrowIfNull(schema);
        RowColumn Require(string name) => schema.Columns.FirstOrDefault(c => c.Name == name)
            ?? throw new ArgumentException($"Column '{name}' is not in the schema.");
        foreach (var filter in Filters)
        {
            var column = Require(filter.Column);
            var op = filter.Operator.Trim().ToLowerInvariant();
            if (op is not ("eq" or "ne" or "gt" or "gte" or "lt" or "lte" or "contains"))
            { throw new ArgumentException($"Operator '{filter.Operator}' is not supported."); }
            if (filter.Value is null || filter.Value.Contains('\\', StringComparison.Ordinal))
            { throw new ArgumentException("A filter value is required and cannot contain a backslash."); }
            if (column.Type == "number" && op != "contains"
                && (!double.TryParse(filter.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)))
            { throw new ArgumentException("A numeric filter requires a finite number."); }
        }
        foreach (var name in Columns.Concat(GroupBy)) { Require(name); }
        foreach (var aggregate in Aggregates)
        {
            var function = aggregate.Function.Trim().ToLowerInvariant();
            if (function is not ("count" or "sum" or "avg" or "min" or "max"))
            { throw new ArgumentException($"Aggregate '{aggregate.Function}' is not supported."); }
            if (function == "count" && string.IsNullOrWhiteSpace(aggregate.Column)) { continue; }
            var column = Require(aggregate.Column ?? "");
            if (function is "sum" or "avg" && column.Type != "number")
            { throw new ArgumentException("Sum and average require a numeric column."); }
        }
    }

    // A view drops the parts its source cannot answer instead of asking the source to pretend.
    public RowQuery Degrade(SourceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var limit = Math.Min(Limit, Math.Max(1, capabilities.MaxPageSize));
        return this with
        {
            Filters = capabilities.Filter ? Filters ?? [] : [],
            Sort = capabilities.Sort ? Sort ?? [] : [],
            GroupBy = capabilities.GroupBy ? GroupBy ?? [] : [],
            Aggregates = capabilities.Aggregate ? Aggregates ?? [] : [],
            Columns = Columns ?? [],
            Limit = limit,
        };
    }
}

[GenerateSerializer, Alias("data.row-filter")]
public sealed record RowFilter(
    [property: Id(0)] string Column,
    [property: Id(1)] string Operator,
    [property: Id(2)] string Value);

[GenerateSerializer, Alias("data.row-sort")]
public sealed record RowSort(
    [property: Id(0)] string Column,
    [property: Id(1)] bool Descending);

[GenerateSerializer, Alias("data.row-aggregate")]
public sealed record RowAggregate(
    [property: Id(0)] string Function,
    [property: Id(1)] string? Column,
    [property: Id(2)] string Alias);
