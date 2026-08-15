namespace DigitalBrain.Abstractions.Corpus;

[GenerateSerializer]
[Alias("db.corpus-appended")]
public sealed record CorpusAppended(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long Sequence) : Synapse;

