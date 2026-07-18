using Ino.Core.Capabilities;
using Orleans;

namespace Ino.Kernel.Contracts;

[GenerateSerializer]
public sealed record RoutingDecision(
    [property: Id(0)] string Prompt,
    [property: Id(1)] RoutingSource Source,
    [property: Id(2)] string? NeuronId,
    [property: Id(3)] double? Confidence,
    [property: Id(4)] DateTimeOffset At,
    [property: Id(5)] double? MlPrediction,
    [property: Id(6)] double? MlConfidence,
    [property: Id(7)] bool LlmCalled,
    [property: Id(8)] int RoutingDurationMs,
    [property: Id(9)] string CorrelationId);
