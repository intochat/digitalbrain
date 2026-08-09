using DigitalBrain.Poc.Abstractions;
using Orleans;

namespace DigitalBrain.Poc.Charting.Contracts;

[GenerateSerializer]
[Alias("db.poc.chart.add-point.v1")]
public sealed record AddChartPoint(
    [property: Id(0)] string ChartId,
    [property: Id(1)] ChartPointDraft Draft) : Synapse;
