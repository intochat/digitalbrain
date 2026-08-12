using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.cancel-schedule")]
public sealed record CancelSchedule(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<ScheduleCancelled>;

