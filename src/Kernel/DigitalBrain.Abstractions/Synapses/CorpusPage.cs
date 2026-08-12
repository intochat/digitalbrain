namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.corpus-page")]
public sealed record CorpusPage(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long Watermark,
    [property: Id(2)] CorpusEntry[] Entries,
    [property: Id(3)] bool Truncated) : Synapse;

