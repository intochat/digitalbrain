using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.remove-vector")]
[Description("Remove a vector memory entry by key")]
public sealed record RemoveVectorMemory(
    [property: Id(0)] VectorMemoryNamespace Namespace,
    [property: Id(1)] string Key) : RequestSynapse<VectorMemoryRemoved>;

[GenerateSerializer]
[Alias("memory.vector-removed")]
[Description("Result of a vector memory remove request")]
public sealed record VectorMemoryRemoved(
    [property: Id(0)] bool Removed,
    [property: Id(1)] VectorMemoryNamespace Namespace,
    [property: Id(2)] string Key) : Synapse;
