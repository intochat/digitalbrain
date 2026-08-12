using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

// Resolves the synapse-graph / registry partition for the current verified principal (A18).
internal static class PrincipalGraph
{
    internal static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? ISynapseGraph.ForPrincipal(owner, actor.PrincipalId)
            : ISynapseGraph.ForOwner(owner);
}

internal static class PrincipalRegistry
{
    internal static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? IRegistry.ForPrincipal(owner, actor.PrincipalId)
            : IRegistry.ForOwner(owner);
}

internal static class PrincipalGrants
{
    internal static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? IGrants.ForPrincipal(owner, actor.PrincipalId)
            : IGrants.ForOwner(owner);
}
