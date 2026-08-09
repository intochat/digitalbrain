using System.ComponentModel;

namespace DigitalBrain.Abstractions;

[ClientEntryPoint]
[Alias("db.synapse-graph")]
[Description("Owner synapse graph: durable runtime routes between neuron instances")]
public partial interface ISynapseGraph :
    INeuron,
    IHandle<Bind>,
    IHandle<Unbind>
{
    const string GrainTypeName = "synapsegraph";
    const string InstanceName = "graph";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    [Alias(nameof(RoutesFor))]
    Task<IReadOnlyCollection<SynapseRoute>> RoutesFor(NeuronId source, string synapseAlias);

    [Alias(nameof(RouteOf))]
    Task<SynapseRoute?> RouteOf(Guid bindingId);

    [Alias(nameof(Bindings))]
    Task<IReadOnlyCollection<SynapseBinding>> Bindings();
}
