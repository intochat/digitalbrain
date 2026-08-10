using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("ui.timer-card")]
[Description("Post a countdown clock card into a chat transcript")]
public sealed record TimerCard(
    [property: Id(0)] string Label,
    [property: Id(1)] DateTimeOffset DueAt) : Synapse;
