using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

// Resolves the synapse-graph / registry partition for the current verified principal (A18).
public static class PrincipalGraph
{
    public static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? ISynapseGraph.ForPrincipal(owner, actor.PrincipalId)
            : ISynapseGraph.ForOwner(owner);
}
