using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Modules.Apps.Tests.E2E;

// Stamps the signed-in principal the way the authenticated HTTP edge does; it travels with every grain call.
internal static class Caller
{
    public static void As(string principal) => CallerContextStamper.Stamp(new CallerContext
    {
        PrincipalId = principal,
        AccountId = "account-" + principal,
        WorkspaceId = "workspace-" + principal,
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    });

    public static void Clear() => Orleans.Runtime.RequestContext.Remove(CallerContextStamper.RequestContextKey);
}
