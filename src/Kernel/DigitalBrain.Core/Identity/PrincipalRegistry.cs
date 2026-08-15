using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public static class PrincipalRegistry
{
    public static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? IRegistry.ForPrincipal(owner, actor.PrincipalId)
            : IRegistry.ForOwner(owner);
}
