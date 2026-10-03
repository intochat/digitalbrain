namespace DigitalBrain.Contracts.Data;

[GenerateSerializer, Alias("data.source-capabilities")]
public sealed record SourceCapabilities(
    [property: Id(0)] bool Filter = true,
    [property: Id(1)] bool Sort = true,
    [property: Id(2)] bool GroupBy = true,
    [property: Id(3)] bool Aggregate = true,
    [property: Id(4)] int MaxPageSize = RowQuery.MaxPageSize);
