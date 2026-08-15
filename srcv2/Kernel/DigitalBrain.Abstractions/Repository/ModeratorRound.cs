namespace DigitalBrain.Abstractions.Repository;

[GenerateSerializer]
[Alias("db.moderator-round")]
public sealed record ModeratorRound(
    [property: Id(0)] int Round,
    [property: Id(1)] string Summary,
    [property: Id(2)] string[] FocusPaths);

