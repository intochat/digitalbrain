namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.journal-read")]
public sealed record JournalRead(
    [property: Id(0)] long ResumeSequence,
    [property: Id(1)] IReadOnlyList<Synapse> Delta,
    [property: Id(2)] JournalSnapshot? ResetSnapshot);
