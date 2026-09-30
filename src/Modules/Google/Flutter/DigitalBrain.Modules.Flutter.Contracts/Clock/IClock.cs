using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Clock;

[Alias("clock"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ClockType)]
public interface IClock : INeuron
{
    Task Set(string label, DateTimeOffset? dueAt);
    [ReadOnly, Alias("read")] Task<ClockState> Read();
}

[GenerateSerializer, Alias("ui.clock-state")]
public sealed class ClockState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Label { get; set; } = "";
    [Id(3)] public DateTimeOffset? DueAt { get; set; }
}
