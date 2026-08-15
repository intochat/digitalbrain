using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.cancel-timer")]
public sealed record CancelTimer(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<TimerCancelled>;

