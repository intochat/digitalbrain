using System.Diagnostics;
using DigitalBrain.Capabilities;
using DigitalBrain.Identity;
using DigitalBrain.Runtime.Orleans.Capabilities;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// Global bidirectional Orleans grain call filter pipeline.
/// Automatically propagates, extracts, and validates capability caller credentials,
/// security principals, tenant brain identity, and distributed tracing activities across grain hops.
/// </summary>
public sealed class DigitalBrainCallFilter : IIncomingGrainCallFilter, IOutgoingGrainCallFilter
{
    private const string ActivityIdKey = "intochatos.activity-id";
    private const string TraceparentKey = "traceparent";

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        // 1. Extract distributed activity trace id if present
        var traceId = RequestContext.Get(ActivityIdKey) as string ?? RequestContext.Get(TraceparentKey) as string;
        if (!string.IsNullOrWhiteSpace(traceId) && Activity.Current is null)
        {
            var activity = new Activity("Orleans.GrainCall").SetParentId(traceId);
            activity.Start();
        }

        // 2. Validate stamped caller context
        if (RuntimeCallerContext.TryGet(out var caller) && caller is not null)
        {
            var targetType = context.TargetId.Type.ToString();
            if (targetType is not null && targetType.StartsWith("neuron", StringComparison.OrdinalIgnoreCase))
            {
                // Verify brain boundary if targeting a brain-scoped grain
                var targetKey = context.TargetId.Key.ToString();
                if (targetKey is not null && CompoundGrainKey.TryParse(targetKey, out var compound))
                {
                    if (!string.Equals(compound.Brain.Value, caller.BrainId, StringComparison.Ordinal)
                        && caller.Kind != CallerKind.Platform
                        && !compound.Brain.IsSystem)
                    {
                        throw new UnauthorizedAccessException(
                            $"Cross-brain security violation: Caller in brain '{caller.BrainId}' cannot invoke grain in brain '{compound.Brain.Value}'.");
                    }
                }
            }
        }

        // 3. Invoke grain turn
        await context.Invoke();
    }

    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        // 1. Automatically propagate current ambient CapabilityCaller across grain boundaries
        if (RuntimeCallerContext.TryGet(out var caller) && caller is not null)
        {
            RequestContext.Set("db.cap.principal", caller.PrincipalId);
            RequestContext.Set("db.cap.account", caller.AccountId);
            RequestContext.Set("db.cap.brain", caller.BrainId);
            if (caller.AppId is not null) RequestContext.Set("db.cap.app", caller.AppId);
            if (caller.ConversationId is not null) RequestContext.Set("db.cap.conversation", caller.ConversationId);
        }

        // 2. Propagate db.caller if present or set from source grain
        if (RequestContext.Get(HostNeuronContext.CallerKey) is null && context.SourceId is { } sourceGrain)
        {
            RequestContext.Set(HostNeuronContext.CallerKey, sourceGrain.Key.ToString() ?? string.Empty);
        }

        // 3. Automatically propagate Activity ID / traceparent
        if (Activity.Current?.Id is { } id)
        {
            RequestContext.Set(ActivityIdKey, id);
            RequestContext.Set(TraceparentKey, id);
        }

        await context.Invoke();
    }
}
