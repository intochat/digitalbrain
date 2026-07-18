namespace Ino.Core.Hosting.ML;

/// <summary>
/// Tunables for <c>NeuronOptimizer</c>. Defaults mirror the design in
/// <c>docs/neuron-ml.md</c> — tweak per-neuron via
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// when a domain has different volume / latency tradeoffs.
/// </summary>
public sealed record NeuronOptimizerOptions
{
    /// <summary>Records required before the first LightGBM train.</summary>
    public int TrainThreshold { get; init; } = 50;

    /// <summary>Records between retrains after the first train.</summary>
    public int RetrainInterval { get; init; } = 25;

    /// <summary>
    /// Hard cap on retained training rows; oldest are evicted when exceeded.
    /// Matches the per-neuron circular-buffer policy from the design doc.
    /// </summary>
    public int MaxRecords { get; init; } = 10_000;
}
