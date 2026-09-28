using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.InfoBar.Signals;

[GenerateSerializer, Alias("ui.infobar-changed")]
public sealed record InfoBarChanged([property: Id(0)] string Name, [property: Id(1)] bool Visible) : Signal;