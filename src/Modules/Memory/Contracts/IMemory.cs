using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Memory;

[Alias("memory")]
public interface IMemory : INeuron
{
    /// <summary>Remembers a note and schedules its storage.</summary>
    [Alias("remember")]
    Task<Accepted<MemoryKey>> Remember(Remember command);

    /// <summary>Forgets a note and schedules its removal.</summary>
    [Alias("forget")]
    Task<Accepted<MemoryKey>> Forget(Forget command);

    /// <summary>Recalls notes matching the query and tags.</summary>
    [ReadOnly]
    [Alias("recall")]
    Task<RecallResult> Recall(Recall query);
}
