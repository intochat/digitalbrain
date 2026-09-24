using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.MyData;

public sealed class SecretResolutionException(string message) : InvalidOperationException(message);

// A secret value is released only inside the outbound call of an app: never to a user turn or a
// read surface, and never without an app identity stamped at a trusted edge.
internal static class SecretResolutionPolicy
{
    internal static void EnsureAllowed(CallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.Kind == CallerKind.User
            || caller.StampedBy is not (TrustedEdge.AppProxy or TrustedEdge.Platform)
            || caller.AppId is not { Length: > 0 })
        {
            throw new SecretResolutionException("A secret resolves only inside the outbound call of an app; user and assistant callers are denied.");
        }
    }
}
