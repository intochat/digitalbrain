using System.ComponentModel;

namespace DigitalBrain.Abstractions;

[ClientEntryPoint]
[Alias("db.synapse-graph")]
[Description("Owner synapse graph: durable runtime connections between neuron instances")]
public partial interface ISynapseGraph :
    INeuron,
    IHandle<Connect>,
    IHandle<Disconnect>
{
    const string GrainTypeName = "synapsegraph";
    const string InstanceName = "graph";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    [Alias(nameof(ConnectionsFrom))]
    Task<IReadOnlyCollection<SynapseConnection>> ConnectionsFrom(NeuronId source, string synapseAlias);

    [Alias(nameof(ConnectionOf))]
    Task<SynapseConnection?> ConnectionOf(Guid connectionId);

    [Alias(nameof(Connections))]
    Task<IReadOnlyCollection<SynapseConnection>> Connections();
}
