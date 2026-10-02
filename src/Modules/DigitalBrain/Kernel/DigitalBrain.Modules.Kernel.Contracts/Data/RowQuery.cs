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
