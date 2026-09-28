namespace DigitalBrain.Memory;

[GenerateSerializer, Alias("memory.purge-namespace")]
public sealed record PurgeNamespace(
    [property: Id(0)] string Namespace);
