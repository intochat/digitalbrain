using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Progress.Signals;

[GenerateSerializer, Alias("ui.progress-changed")]
public sealed record ProgressChanged([property: Id(0)] string Name, [property: Id(1)] double Value) : Signal;