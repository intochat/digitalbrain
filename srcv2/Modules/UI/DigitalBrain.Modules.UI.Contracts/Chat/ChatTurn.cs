using DigitalBrain.UI;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turn")]
public sealed record ChatTurn(
    [property: Id(0)] bool FromUser,
    [property: Id(1)] string Text,
    [property: Id(2)] ChatButtonOffer[]? Buttons = null,
    [property: Id(3)] ChatChartOffer[]? Charts = null,
    [property: Id(4)] ChatTimerOffer[]? Timers = null);
