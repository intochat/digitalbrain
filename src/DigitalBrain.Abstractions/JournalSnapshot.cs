namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.journal-tally")]
public sealed record JournalTally(
    [property: Id(0)] string SynapseType,
    [property: Id(1)] long Recorded);

[GenerateSerializer]
[Alias("db.journal-snapshot")]
public sealed record JournalSnapshot(
    [property: Id(0)] long TotalRecorded,
    [property: Id(1)] long LastSequence,
    [property: Id(2)] long EarliestRetainedSequence,
    [property: Id(3)] int RetainedCount,
    [property: Id(4)] IReadOnlyList<JournalTally> Tallies)
{
    public long RecordedOf(string synapseType)
        => Tallies.FirstOrDefault(tally => string.Equals(tally.SynapseType, synapseType, StringComparison.Ordinal))?.Recorded ?? 0;
}
