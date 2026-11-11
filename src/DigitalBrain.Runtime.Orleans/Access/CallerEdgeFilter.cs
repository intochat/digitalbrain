using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Access;

/// <summary>
/// Enforces unforgeable edge caller invariants:
/// Ensures incoming client calls cannot claim Inside=true or Via != null.
/// Ensures incoming external client calls cannot claim internal or invalid actors (Kernel, Scheduler, Module).
/// Sets Inside based on whether SourceId is an internal cluster grain (not sys.client and not null).
/// </summary>
internal sealed class CallerEdgeFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        var raw = RequestContext.Get(OrleansCallerAmbient.Key);
        if (raw is CallerWire wire)
        {
            var isInternalGrain = context.SourceId is { } source
                && !source.Type.ToString()!.StartsWith("sys.client", StringComparison.OrdinalIgnoreCase);

            if (!isInternalGrain)
            {
                // External client: cannot forge Inside or Via
                wire.Inside = false;
                wire.Via = null;

                // External client: cannot claim internal or invalid actor kinds
                if (wire.Actor is not (CallerWire.ActorPerson or CallerWire.ActorAgent or CallerWire.ActorApp))
                {
                    throw new UnauthorizedAccessException(
                        $"External client cannot claim actor kind {wire.Actor}. Only Person ({CallerWire.ActorPerson}), Agent ({CallerWire.ActorAgent}), and App ({CallerWire.ActorApp}) are permitted from external callers.");
                }
            }
            else
            {
                // Internal grain: always inside
                wire.Inside = true;
            }
        }

        await context.Invoke();
    }
}
