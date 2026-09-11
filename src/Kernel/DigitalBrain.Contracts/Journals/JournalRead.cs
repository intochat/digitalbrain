using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Journals;

[GenerateSerializer]
[Alias("db.journal-read")]
public sealed record JournalRead(
    [property: Id(0)] long ResumeSequence,
    [property: Id(1)] long EarliestRetained,
    [property: Id(2)] bool Gap,
    [property: Id(3)] IReadOnlyList<SignalDelivery> Delta,
    [property: Id(4)] JournalSnapshot? ResetSnapshot,
    [property: Id(5)] long TotalRecorded);
