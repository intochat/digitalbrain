using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public static class PrincipalGrants
{
    public static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? IGrants.ForPrincipal(owner, actor.PrincipalId)
            : IGrants.ForOwner(owner);
}
