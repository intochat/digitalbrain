using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Memory;

[Alias("memory")]
public interface IMemory : INeuron
{
    Task<MemoryKey> Remember(Remember note);

    Task<MemoryKey> Forget(Forget note);

    Task<long> PurgeNamespace(PurgeNamespace note);

    /// <summary>Copies one bounded legacy index page into neuron-owned state without deleting the source.</summary>
    Task<MemoryImportResult> ImportLegacy(string @namespace, string? cursor = null, int limit = 128);

    /// <summary>Rebuilds the optional search index from canonical neuron pages.</summary>
    Task<MemoryIndexResult> RebuildIndex(string @namespace);

    [ReadOnly]
    Task<RecallResult> Recall(Recall query);
}

[GenerateSerializer, Alias("memory.import-result")]
public sealed record MemoryImportResult([property: Id(0)] int Imported, [property: Id(1)] string? NextCursor);

[GenerateSerializer, Alias("memory.index-result")]
public sealed record MemoryIndexResult([property: Id(0)] bool Available, [property: Id(1)] int Indexed, [property: Id(2)] int Pending);
