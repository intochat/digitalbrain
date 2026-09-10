using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.activity-execution-changed")]
public sealed record ActivityExecutionChanged(
    [property: Id(0)] CorrelationId CorrelationId,
    [property: Id(1)] string OperationId,
    [property: Id(2)] SignalId SignalId,
    [property: Id(3)] SignalId? CausationId,
    [property: Id(4)] NeuronId Source,
    [property: Id(5)] NeuronId? Target,
    [property: Id(6)] string SignalType,
    [property: Id(7)] string Phase,
    [property: Id(8)] DateTimeOffset Timestamp,
    [property: Id(9)] string? Title = null,
    [property: Id(10)] string? CommandId = null,
    [property: Id(11)] string? Detail = null);
