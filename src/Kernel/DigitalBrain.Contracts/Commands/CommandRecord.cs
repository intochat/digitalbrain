using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command-record")]
public sealed record CommandRecord(
    [property: Id(0)] long Sequence,
    [property: Id(1)] CommandId Id,
    [property: Id(2)] int Incarnation,
    [property: Id(3)] string Interface,
    [property: Id(4)] string Method,
    [property: Id(5)] CommandPhase Phase,
    [property: Id(6)] NeuronId Caller,
    [property: Id(7)] CorrelationId Correlation,
    [property: Id(8)] SignalId? Causation,
    [property: Id(9)] string? ArgsJson,
    [property: Id(10)] string? ResultJson,
    [property: Id(11)] string? Error,
    [property: Id(12)] DateTimeOffset At,
    // Invariant 11's other half: the fired signal's causation is the work id, whose command is discoverable here; SignalDelivery stays unchanged.
    [property: Id(13)] IReadOnlyList<SignalId>? ScheduledWork = null);
