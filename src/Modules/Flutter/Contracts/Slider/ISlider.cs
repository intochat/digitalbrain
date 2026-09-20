using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Slider;

[Alias("slider"), Orleans.Metadata.DefaultGrainType(UIVocabulary.SliderType)]
public interface ISlider : INeuron
{
    Task Configure(double min, double max, double step);
    Task SetValue(double value);
    [ReadOnly, Alias("read")] Task<SliderState> Read();
}

[GenerateSerializer, Alias("ui.slider-state")]
public sealed class SliderState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public double Min { get; set; }
    [Id(3)] public double Max { get; set; } = 1;
    [Id(4)] public double Step { get; set; } = 1;
    [Id(5)] public double Value { get; set; }
}
