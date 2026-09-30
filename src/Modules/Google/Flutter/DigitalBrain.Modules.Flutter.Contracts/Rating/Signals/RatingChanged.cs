using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Rating.Signals;

[GenerateSerializer, Alias("ui.rating-changed")]
public sealed record RatingChanged([property: Id(0)] string Name, [property: Id(1)] int Value) : Signal;
