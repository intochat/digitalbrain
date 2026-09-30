using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Image.Signals;

[GenerateSerializer, Alias("ui.image-changed")]
public sealed record ImageChanged([property: Id(0)] string Name, [property: Id(1)] int Version) : Signal;
