using Orleans;

namespace DigitalBrain.Poc.Charting.Contracts;

[GenerateSerializer]
[Alias("db.poc.chart.point-draft.v1")]
public sealed record ChartPointDraft(
    [property: Id(0)] string SourcePostId,
    [property: Id(1)] System.DateTimeOffset OccurredAt);
