using DigitalBrain.Contracts;

namespace DigitalBrain.Memory.Signals;

[GenerateSerializer, Alias("memory.namespace-purged")]
public sealed record NamespacePurged(
    [property: Id(0)] string Namespace,
    [property: Id(1)] long RemovedCount) : Signal;
