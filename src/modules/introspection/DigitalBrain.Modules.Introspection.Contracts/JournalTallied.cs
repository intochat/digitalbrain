using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.journal-tallied")]
[Description("How many synapses of each type a neuron journal has recorded, or why the tally was refused")]
public sealed record JournalTallied(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject,
    [property: Id(2)] string Direction,
    [property: Id(3)] long TotalRecorded,
    [property: Id(4)] long LastSequence,
    [property: Id(5)] IReadOnlyList<JournalTally> Tallies,
    [property: Id(6)] string? Error = null) : Synapse
{
    public static JournalTallied Refused(CommandId commandId, NeuronId subject, string direction, string reason)
        => new(commandId, subject, direction, TotalRecorded: 0, LastSequence: 0, [], reason);
}
