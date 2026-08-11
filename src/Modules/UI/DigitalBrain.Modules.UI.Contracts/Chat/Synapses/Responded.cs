using DigitalBrain.Abstractions;
using DigitalBrain.UI;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.responded")]
public sealed record Responded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Chat,
    [property: Id(2)] string Text,
    [property: Id(3)] ChatButtonOffer[]? Buttons = null,
    [property: Id(4)] ChatChartOffer[]? Charts = null,
    [property: Id(5)] ChatTimerOffer[]? Timers = null,
    [property: Id(6)] string Author = "") : Synapse;
