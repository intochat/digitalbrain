using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Color;

[Alias("color"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ColorType)]
public interface IColor : INeuron
{
    Task Set(string hex);
    [ReadOnly, Alias("read")] Task<ColorState> Read();
}

[GenerateSerializer, Alias("ui.color-state")]
public sealed class ColorState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Hex { get; set; } = "#000000";
}
