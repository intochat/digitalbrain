using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.activity-event-view")]
public sealed record ActivityEventView(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string SignalId,
    [property: Id(2)] string? CausationId,
    [property: Id(3)] string SourceNeuronId,
    [property: Id(4)] string? TargetNeuronId,
    [property: Id(5)] string SignalType,
    [property: Id(6)] string Phase,
    [property: Id(7)] DateTimeOffset Timestamp,
    [property: Id(8)] string? Detail = null);
