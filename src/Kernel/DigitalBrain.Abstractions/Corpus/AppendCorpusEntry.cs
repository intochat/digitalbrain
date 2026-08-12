namespace DigitalBrain.Abstractions.Corpus;

[GenerateSerializer]
[Alias("db.append-corpus-entry")]
public sealed record AppendCorpusEntry(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Text,
    [property: Id(3)] string? Correlation = null,
    [property: Id(4)] DateTimeOffset? At = null) : RequestSynapse<CorpusAppended>;

