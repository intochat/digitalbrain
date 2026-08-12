
namespace DigitalBrain.Abstractions.Graph;

[ClientEntryPoint]
[Alias("db.synapse-graph")]
public partial interface ISynapseGraph :
    INeuron,
    IHandle<Connect>,
    IHandle<Disconnect>
{
    const string GrainTypeName = "synapsegraph";
    const string InstanceName = "graph";

    // Owner-wide graph (bootstrap / unattributed system turns). Prefer ForPrincipal.
    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    // A18: each principal has their own graph partition under the owner.
    static NeuronId ForPrincipal(OwnerId owner, PrincipalId principal)
        => new(GrainTypeName, owner, PrincipalPartition.InstanceName(principal, InstanceName));

    [Alias(nameof(ConnectionsFrom))]
    Task<IReadOnlyCollection<SynapseConnection>> ConnectionsFrom(NeuronId source, string synapseAlias);

    [Alias(nameof(ConnectionOf))]
    Task<SynapseConnection?> ConnectionOf(Guid connectionId);

    [Alias(nameof(Connections))]
    Task<IReadOnlyCollection<SynapseConnection>> Connections();
}
