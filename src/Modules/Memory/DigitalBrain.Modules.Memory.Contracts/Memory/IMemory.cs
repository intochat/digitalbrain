using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Memory;

[Alias("memory")]
public interface IMemory : INeuron
{
    Task<MemoryKey> Remember(Remember note);

    Task<MemoryKey> Forget(Forget note);

    Task<long> PurgeNamespace(PurgeNamespace note);

    Task<MemoryIndexResult> RebuildIndex(string @namespace);

    [ReadOnly]
    Task<RecallResult> Recall(Recall query);
}

[GenerateSerializer, Alias("memory.index-result")]
public sealed record MemoryIndexResult([property: Id(0)] bool Available, [property: Id(1)] int Indexed, [property: Id(2)] int Pending);
