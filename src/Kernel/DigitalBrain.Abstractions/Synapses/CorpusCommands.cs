namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.append-corpus-entry")]
public sealed record AppendCorpusEntry(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Text,
    [property: Id(3)] string? Correlation = null,
    [property: Id(4)] DateTimeOffset? At = null) : RequestSynapse<CorpusAppended>;

[GenerateSerializer]
[Alias("db.corpus-appended")]
public sealed record CorpusAppended(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long Sequence) : Synapse;

[GenerateSerializer]
[Alias("db.read-corpus")]
public sealed record ReadCorpus(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long AfterSequence = 0,
    [property: Id(2)] int Limit = 100) : RequestSynapse<CorpusPage>;

[GenerateSerializer]
[Alias("db.corpus-page")]
public sealed record CorpusPage(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long Watermark,
    [property: Id(2)] CorpusEntry[] Entries,
    [property: Id(3)] bool Truncated) : Synapse;

[GenerateSerializer]
[Alias("db.read-episode")]
public sealed record ReadEpisode(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Correlation,
    [property: Id(2)] int Limit = 100) : RequestSynapse<EpisodePage>;

[GenerateSerializer]
[Alias("db.episode-page")]
public sealed record EpisodePage(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Correlation,
    [property: Id(2)] CorpusEntry[] Entries) : Synapse;

[GenerateSerializer]
[Alias("db.corpus-entry")]
public sealed record CorpusEntry(
    [property: Id(0)] long Sequence,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Text,
    [property: Id(3)] string? Correlation,
    [property: Id(4)] DateTimeOffset At);
