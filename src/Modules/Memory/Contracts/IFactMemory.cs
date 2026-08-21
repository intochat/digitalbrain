using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Memory;

// Watermarked, resumable owner (or principal) projection of durable story facts — the
// single long-term-memory concept.
[Alias("memory.facts")]
public partial interface IFactMemory :
    INeuron,
    IHandle<StoreFact>,
    IHandle<ReadFacts>
{
    const string GrainTypeName = "factmemory";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    static NeuronId ForPrincipal(OwnerId owner, PrincipalId principal)
        => new(GrainTypeName, owner, PrincipalPartition.InstanceName(principal, InstanceName));
}
