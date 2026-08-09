using Orleans;

namespace DigitalBrain.Poc.Charting.Contracts;

[GenerateSerializer]
[Alias("db.poc.chart.point.v1")]
public sealed record ChartPoint(
    [property: Id(0)] string SourcePostId,
    [property: Id(1)] System.DateTimeOffset OccurredAt,
    [property: Id(2)] int Ordinal);
