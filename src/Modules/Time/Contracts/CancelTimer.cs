using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;
namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.cancel-timer")]
public sealed record CancelTimer(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<TimerCancelled>;

