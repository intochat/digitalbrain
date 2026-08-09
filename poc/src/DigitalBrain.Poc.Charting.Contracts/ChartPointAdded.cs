using DigitalBrain.Poc.Abstractions;
using Orleans;

namespace DigitalBrain.Poc.Charting.Contracts;

[GenerateSerializer]
[Alias("db.poc.chart.point-added.v1")]
public sealed record ChartPointAdded(
    [property: Id(0)] string ChartId,
    [property: Id(1)] ChartPoint Point,
    [property: Id(2)] string EffectId) : Synapse;
