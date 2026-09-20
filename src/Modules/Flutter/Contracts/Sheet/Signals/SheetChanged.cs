using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Sheet.Signals;

[GenerateSerializer, Alias("ui.sheet-changed")]
public sealed record SheetChanged([property: Id(0)] string Name, [property: Id(1)] int Version) : Signal;
