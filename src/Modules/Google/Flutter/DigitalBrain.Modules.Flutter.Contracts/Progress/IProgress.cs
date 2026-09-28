using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Progress;

[Alias("progress"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ProgressType)]
public interface IProgress : INeuron
{
    Task Set(bool determinate, double value, string label);
    [ReadOnly, Alias("read")] Task<ProgressState> Read();
}

[GenerateSerializer, Alias("ui.progress-state")]
public sealed class ProgressState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public bool Determinate { get; set; }
    [Id(3)] public double Value { get; set; }
    [Id(4)] public string Label { get; set; } = "";
}