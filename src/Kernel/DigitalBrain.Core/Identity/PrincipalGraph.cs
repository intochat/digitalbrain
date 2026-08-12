using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

// Resolves the synapse-graph partition for the current verified principal (A18).
public static class PrincipalGraph
{
    public static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? ISynapseGraph.ForPrincipal(owner, actor.PrincipalId)
            : ISynapseGraph.ForOwner(owner);

    // Prefer the subject's own principal partition (chat/button instance name), then ambient.
    public static NeuronId ResolveFor(NeuronId subject)
    {
        if (PrincipalPartition.TryParse(subject.Name, out var principal, out _))
        {
            return ISynapseGraph.ForPrincipal(subject.Owner, principal);
        }

        return Resolve(subject.Owner);
    }
}
