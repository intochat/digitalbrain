using System.Text.Json;
using Orleans;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>What a gate neuron answered for one signal, as it crosses the grain boundary.</summary>
/// <param name="Allowed">Whether the gate allows the signal.</param>
/// <param name="Rules">The names of the broken rules when refused; empty when allowed.</param>
/// <param name="Messages">The messages of the broken rules, in the same order.</param>
/// <param name="Next">The proposed next state of the gate neuron as JSON when allowed; null when refused.</param>
[GenerateSerializer, Alias("synapse.gate-verdict")]
public sealed record GateVerdict(
    [property: Id(0)] bool Allowed,
    [property: Id(1)] string[] Rules,
    [property: Id(2)] string[] Messages,
    [property: Id(3)] JsonElement? Next)
{
    /// <summary>The ticket of the gate's provisional entry when allowed, handed back to <c>Committed</c> or <c>Aborted</c>; empty when refused.</summary>
    [Id(4)]
    public Guid Ticket { get; init; }
}
