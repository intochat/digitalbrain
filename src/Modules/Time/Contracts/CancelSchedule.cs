using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;
namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.cancel-schedule")]
public sealed record CancelSchedule(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<ScheduleCancelled>;

