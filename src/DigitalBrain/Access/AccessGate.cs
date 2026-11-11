using DigitalBrain.Invocation;

namespace DigitalBrain.Access;

/// <summary>
/// Synchronous admission gate for neuron operations. Evaluated after binding and before neuron code executes.
/// </summary>
internal sealed class AccessGate(ICallerAmbient callers, AccessOptions options, AccessCounters counters)
{
    /// <summary>
    /// Evaluates access permissions for the target neuron and bound call.
    /// </summary>
    /// <returns>The caller under which the operation runs.</returns>
    /// <exception cref="UnauthorizedAccessException">Access is denied or required caller is absent.</exception>
    public Caller Admit(NeuronId target, BoundCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var exemptToday = options.AuditContractInternal
            && (!call.Contract.IsVisible || call.Contract.IsDefined(typeof(InternalAttribute), inherit: false));

        if (callers.Current is not { } caller)
        {
            if (options.UnstampedEdgeIsKernel)
            {
                counters.ImplicitKernel(call.Contract.Name, call.Method.Name);
                return Caller.OfKernel(target.Brain);
            }

            if (!exemptToday)
            {
                throw new UnauthorizedAccessException($"{call.Contract.Name}.{call.Method.Name} on {target}: the call carries no caller.");
            }

            counters.WouldDeny(call.Contract.Name, call.Method.Name, AccessDenial.NoCaller);
            return Caller.OfKernel(target.Brain);
        }

        var decision = AccessPolicy.Decide(caller, target, call.Access);
        if (decision.Allowed) { return caller; }

        if (exemptToday)
        {
            counters.WouldDeny(call.Contract.Name, call.Method.Name, decision.Denial);
            return caller;
        }

        throw new UnauthorizedAccessException($"{decision.Explanation} ({call.Contract.Name}.{call.Method.Name} on {target.Type})");
    }
}
