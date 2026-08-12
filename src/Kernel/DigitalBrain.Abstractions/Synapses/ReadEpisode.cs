namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.read-episode")]
public sealed record ReadEpisode(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Correlation,
    [property: Id(2)] int Limit = 100) : RequestSynapse<EpisodePage>;

