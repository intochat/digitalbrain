namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer]
[Alias("db.episode-page")]
public sealed record EpisodePage(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Correlation,
    [property: Id(2)] CorpusEntry[] Entries) : Synapse;

