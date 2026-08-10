using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-scheduled")]
[Description("The timer is armed; carries the due instant and the note it will deliver")]
public sealed record TimerScheduled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Timer,
    [property: Id(2)] long Generation,
    [property: Id(3)] DateTimeOffset ScheduledAt,
    [property: Id(4)] DateTimeOffset DueAt,
    [property: Id(5)] TimeSpan Duration,
    [property: Id(6)] string Note) : Synapse;

[GenerateSerializer]
[Alias("time.timer-elapsed")]
[Description("The timer reached its due instant and delivers its note")]
public sealed record TimerElapsed(
    [property: Id(0)] NeuronId Timer,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset ScheduledAt,
    [property: Id(3)] DateTimeOffset DueAt,
    [property: Id(4)] DateTimeOffset ObservedAt,
    [property: Id(5)] TimerResolution Resolution,
    [property: Id(6)] string Note) : Synapse;

[GenerateSerializer]
[Alias("time.timer-cancelled")]
[Description("The scheduled timer was cancelled before it elapsed")]
public sealed record TimerCancelled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Timer,
    [property: Id(2)] long Generation) : Synapse;
