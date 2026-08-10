using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.start-timer")]
[Description("Arm the timer for a number of seconds; the note is delivered when it elapses")]
public sealed record StartTimer(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] int DurationSeconds,
    [property: Id(2)] string Note) : RequestSynapse<TimerScheduled>;

[GenerateSerializer]
[Alias("time.cancel-timer")]
[Description("Cancel the scheduled timer before it elapses")]
public sealed record CancelTimer(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<TimerCancelled>;
