namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.journal-tally")]
public sealed record JournalTally(
    [property: Id(0)] string SynapseType,
    [property: Id(1)] long Recorded);
