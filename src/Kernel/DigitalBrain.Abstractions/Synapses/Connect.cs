using System.ComponentModel;

namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.connect")]
[Description("Create or replace a synapse connection: deliver a source neuron's emitted synapses to a target neuron, optionally through a named transform, optionally until an expiry")]
public sealed record Connect(
    [property: Id(0)] Guid ConnectionId,
    [property: Id(1)] NeuronId Source,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] NeuronId Target,
    [property: Id(4)] string? Transform = null,
    [property: Id(5)] DateTimeOffset? ExpiresAt = null) : RequestSynapse<Connected>;

[GenerateSerializer]
[Alias("db.connected")]
[Description("A synapse connection is live")]
public sealed record Connected(
    [property: Id(0)] Guid ConnectionId,
    [property: Id(1)] NeuronId Source,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] NeuronId Target) : Synapse;
