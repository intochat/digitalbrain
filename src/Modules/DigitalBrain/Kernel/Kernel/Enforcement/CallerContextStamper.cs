using DigitalBrain.Contracts.Enforcement;
using Orleans.Runtime;

namespace DigitalBrain.Core.Enforcement;

// A caller context is stamped only at a trusted edge: authenticated HTTP, the app proxy or the
// scheduler. Stamping validates that the claimed edge may speak for that caller kind, then places
// the context on the Orleans RequestContext so it travels with the grain call. A principal that
// did not pass through this gate is rejected by the call filter as untrusted.
public static class CallerContextStamper
{
    public const string RequestContextKey = "intochat.caller-context";

    public static CallerContext Stamp(CallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsTrusted(context))
        {
            throw new UntrustedCallerException(
                $"Caller kind {context.Kind} cannot be stamped by {context.StampedBy}.");
        }

        RequestContext.Set(RequestContextKey, context);
        return context;
    }

    public static bool TryGet(out CallerContext? context)
    {
        context = RequestContext.Get(RequestContextKey) as CallerContext;
        return context is not null;
    }

    public static CallerContext Require()
        => TryGet(out var context) && context is not null
            ? context
            : throw new UntrustedCallerException("No caller context was stamped at a trusted edge.");

    public static bool IsTrusted(CallerContext context) =>
        !string.IsNullOrWhiteSpace(context.PrincipalId)
        && !string.IsNullOrWhiteSpace(context.AccountId)
        && !string.IsNullOrWhiteSpace(context.WorkspaceId)
        && Enum.IsDefined(context.Kind)
        && Enum.IsDefined(context.StampedBy)
        && IsEdgeAllowed(context.Kind, context.StampedBy);

    private static bool IsEdgeAllowed(CallerKind kind, TrustedEdge edge) => kind switch
    {
        CallerKind.User or CallerKind.Assistant => edge == TrustedEdge.AuthenticatedHttp,
        CallerKind.App => edge == TrustedEdge.AppProxy,
        CallerKind.Scheduler => edge == TrustedEdge.Scheduler,
        CallerKind.Platform => edge == TrustedEdge.Platform,
        _ => false,
    };
}

public sealed class UntrustedCallerException(string message) : Exception(message);
