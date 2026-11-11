namespace DigitalBrain.Access;

/// <summary>
/// The pure decision "may this caller call this operation of this neuron". Pure: zero ambient reads, zero I/O.
/// </summary>
public static class AccessPolicy
{
    /// <summary>
    /// Decides whether <paramref name="caller"/> is authorized to invoke <paramref name="target"/> under <paramref name="access"/>.
    /// </summary>
    public static AccessDecision Decide(Caller caller, NeuronId target, OperationAccess access)
    {
        ArgumentNullException.ThrowIfNull(caller);
        switch (caller.Actor)
        {
            case Actor.Kernel:
                return AccessDecision.Allow;
            case Actor.Person or Actor.Agent or Actor.App or Actor.Module or Actor.Scheduler:
                break;
            default:
                return AccessDecision.Deny(AccessDenial.UnknownActor, "The caller's actor is not one the kernel knows.");
        }

        if (caller.Brain != target.Brain)
        {
            return AccessDecision.Deny(AccessDenial.OutsideBrain,
                $"A caller acting in brain {caller.Brain.Value} may not call a neuron of brain {target.Brain.Value}.");
        }

        return access == OperationAccess.Internal && !caller.Inside && caller.Actor is not Actor.Module
            ? AccessDecision.Deny(AccessDenial.InternalOnly, $"{target.Type} is reachable only from inside the cluster.")
            : AccessDecision.Allow;
    }
}
