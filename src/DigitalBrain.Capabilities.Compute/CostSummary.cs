using DigitalBrain.Neurons;

namespace DigitalBrain.Capabilities.Compute;

public enum UsageGrouping
{
    Origin = 0,
    App = 1,
    Synapse = 2,
    Neuron = 3,
    Kind = 4,
}

public sealed record CostRow(
    string Key,
    long Micros,
    long Count)
{
    public long? KnownListPriceMicros { get; init; }
    public long? UnpricedCount { get; init; }
    public long? PendingCount { get; init; }
    public long? UnknownCount { get; init; }
}

public sealed record CostSummary(
    long BalanceMicros,
    long SpentMicros,
    CostRow[] Rows)
{
    public long? KnownListPriceMicros { get; init; }
    public long? UnpricedCount { get; init; }
    public long? PendingCount { get; init; }
    public long? UnknownCount { get; init; }
}
