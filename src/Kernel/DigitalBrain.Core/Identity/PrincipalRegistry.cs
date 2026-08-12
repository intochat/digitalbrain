using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

// Resolves the instance-registry partition for the current verified principal (A18).
public static class PrincipalRegistry
{
    public static NeuronId Resolve(OwnerId owner)
        => VerifiedActor.Current is { } actor
            ? IRegistry.ForPrincipal(owner, actor.PrincipalId)
            : IRegistry.ForOwner(owner);

    public static NeuronId ResolveFor(NeuronId subject)
    {
        if (PrincipalPartition.TryParse(subject.Name, out var principal, out _))
        {
            return IRegistry.ForPrincipal(subject.Owner, principal);
        }

        return Resolve(subject.Owner);
    }
}
