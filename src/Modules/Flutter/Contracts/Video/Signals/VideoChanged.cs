using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Video.Signals;

[GenerateSerializer, Alias("ui.video-changed")]
public sealed record VideoChanged([property: Id(0)] string Name, [property: Id(1)] bool Playing) : Signal;
