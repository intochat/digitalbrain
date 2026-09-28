using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Tabs.Signals;

[GenerateSerializer, Alias("ui.tabs-changed")]
public sealed record TabsChanged([property: Id(0)] string Name, [property: Id(1)] string SelectedId) : Signal;