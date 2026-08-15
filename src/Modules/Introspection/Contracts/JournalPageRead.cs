using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.journal-page-read")]
public sealed record JournalPageRead(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject,
    [property: Id(2)] string Direction,
    [property: Id(3)] long ResumeSequence,
    [property: Id(4)] bool Compacted,
    [property: Id(5)] IReadOnlyList<JournaledFact> Entries,
    [property: Id(6)] string? Error = null) : Synapse
{
    public static JournalPageRead Refused(CommandId commandId, NeuronId subject, string direction, string reason)
        => new(commandId, subject, direction, ResumeSequence: 0, Compacted: false, [], reason);
}
