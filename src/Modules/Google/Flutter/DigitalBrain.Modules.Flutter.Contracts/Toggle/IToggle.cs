using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Toggle;

[Alias("toggle"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ToggleType)]
public interface IToggle : INeuron
{
    Task Set(string label, bool on);
    Task Flip();
    [ReadOnly, Alias("read")] Task<ToggleState> Read();
}

[GenerateSerializer, Alias("ui.toggle-state")]
public sealed class ToggleState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Label { get; set; } = "";
    [Id(3)] public bool On { get; set; }
}
