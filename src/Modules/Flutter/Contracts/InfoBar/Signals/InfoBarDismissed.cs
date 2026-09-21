using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.InfoBar.Signals;

[GenerateSerializer, Alias("ui.infobar-dismissed")]
public sealed record InfoBarDismissed([property: Id(0)] string Name) : Signal;