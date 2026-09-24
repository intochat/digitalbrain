namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.meter")]
public sealed record AppMeter
{
    [Id(0)] public required string MeterId { get; init; }
    [Id(1)] public required string Unit { get; init; }
    [Id(2)] public required string Aggregation { get; init; }
    [Id(3)] public decimal? ProposedPriceInCompute { get; init; }
}
