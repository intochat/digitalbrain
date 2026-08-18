namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.facts-read")]
public sealed record FactsRead(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long Watermark,
    [property: Id(2)] FactEntry[] Facts,
    [property: Id(3)] bool Truncated) : Synapse;
