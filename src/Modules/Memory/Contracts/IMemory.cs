using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Memory;

[Alias("memory")]
public interface IMemory : INeuron
{
    /// <summary>Remembers a note and schedules its storage.</summary>
    [Alias("remember")]
    [NeuronTool]
    Task<Accepted<MemoryKey>> Remember(Remember command);

    /// <summary>Forgets a note and schedules its removal.</summary>
    [Alias("forget")]
    [NeuronTool]
    Task<Accepted<MemoryKey>> Forget(Forget command);

    /// <summary>Recalls notes matching the query and tags.</summary>
    [ReadOnly]
    [Alias("recall")]
    [NeuronTool(IsReadOnly = true)]
    Task<RecallResult> Recall(Recall query);
}
