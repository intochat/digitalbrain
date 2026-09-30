using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Slider.Signals;

[GenerateSerializer, Alias("ui.slider-changed")]
public sealed record SliderChanged([property: Id(0)] string Name, [property: Id(1)] double Value) : Signal;
