using System.ComponentModel;

namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.bind")]
[Description("Create or replace a synapse route: deliver a source neuron's emitted synapses to a target neuron, optionally through a named transform, optionally until an expiry")]
public sealed record Bind(
    [property: Id(0)] Guid BindingId,
    [property: Id(1)] NeuronId Source,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] NeuronId Target,
    [property: Id(4)] string? Transform = null,
    [property: Id(5)] DateTimeOffset? ExpiresAt = null) : RequestSynapse<Bound>;

[GenerateSerializer]
[Alias("db.bound")]
[Description("A synapse route is live")]
public sealed record Bound(
    [property: Id(0)] Guid BindingId,
    [property: Id(1)] NeuronId Source,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] NeuronId Target) : Synapse;
