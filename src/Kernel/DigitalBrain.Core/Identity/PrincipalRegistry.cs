using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

internal static class PrincipalRegistry
{
    internal static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? IRegistry.ForPrincipal(owner, actor.PrincipalId)
            : IRegistry.ForOwner(owner);
}

