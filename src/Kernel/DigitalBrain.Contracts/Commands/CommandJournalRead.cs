namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command-journal-read")]
public sealed record CommandJournalRead(
    [property: Id(0)] long ResumeSequence,
    [property: Id(1)] long EarliestRetained,
    [property: Id(2)] bool Gap,
    [property: Id(3)] IReadOnlyList<CommandRecord> Delta);
