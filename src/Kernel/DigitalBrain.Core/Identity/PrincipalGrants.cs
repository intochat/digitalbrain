using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

internal static class PrincipalGrants
{
    internal static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? IGrants.ForPrincipal(owner, actor.PrincipalId)
            : IGrants.ForOwner(owner);
}

