using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("ui.timer-card")]
public sealed record TimerCard(
    [property: Id(0)] string Label,
    [property: Id(1)] DateTimeOffset DueAt) : Synapse;
