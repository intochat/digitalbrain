using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;

namespace DigitalBrain.Platform.Identity.Grants;

// Pure grant rules for the call-filter stage. Kept free of Orleans so the enforcement decision
// can be tested directly: a principal may only act inside the workspace it is a member of, and an
// app caller needs a live grant for every semantic type it touches. A denied value is
// indistinguishable from a missing one.
public static class GrantRules
{
    public static CallDecision? Evaluate(CallRequest request, Member? member, IReadOnlyList<Grant> grants)
    {
        ArgumentNullException.ThrowIfNull(request);
        grants ??= [];

        var caller = request.Caller;
        if (caller.Kind is CallerKind.User or CallerKind.Assistant
            && (member is null || member.PrincipalId != caller.PrincipalId || member.AccountId != caller.AccountId || member.BrainId != caller.BrainId))
        {
            return CallDecision.Deny(CallDenial.OutsideWorkspace, "The principal is not a member of this account and brain.");
        }

        if (caller.Kind != CallerKind.App || request.SemanticTypeIds.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(caller.AppId))
        {
            return CallDecision.Deny(CallDenial.UntrustedCaller, "An app caller must identify its app.");
        }

        foreach (var semanticTypeId in request.SemanticTypeIds)
        {
            if (!IsGranted(caller, semanticTypeId, grants))
            {
                return CallDecision.Deny(CallDenial.MissingGrant,
                    $"No grant for semantic type '{semanticTypeId}'. The value is treated as missing.");
            }
        }

        return CallDecision.Allow();
    }

    // The one-time grants a successful app read just spent. An always grant is left in place; only
    // a live Once grant for a requested semantic type is returned, so the store clears exactly those.
    public static IReadOnlyList<Grant> OnceToConsume(CallRequest request, IReadOnlyList<Grant> grants)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (grants is null || grants.Count == 0 || request.SemanticTypeIds.Length == 0) { return []; }

        var caller = request.Caller;
        if (caller.Kind != CallerKind.App || string.IsNullOrWhiteSpace(caller.AppId)) { return []; }

        return
        [
            .. grants.Where(grant => !grant.Revoked
                && grant.Mode == GrantMode.Once
                && string.Equals(grant.WorkspaceId, caller.BrainId, StringComparison.Ordinal)
                && string.Equals(grant.AppId, caller.AppId, StringComparison.Ordinal)
                && request.SemanticTypeIds.Contains(grant.SemanticTypeId, StringComparer.Ordinal)
                && !grants.Any(other => other.Mode != GrantMode.Once && !other.Revoked
                    && other.WorkspaceId == grant.WorkspaceId && other.AppId == grant.AppId
                    && other.SemanticTypeId == grant.SemanticTypeId && ModeSatisfied(other, caller))),
        ];
    }

    internal static bool IsGranted(CallerContext caller, string semanticTypeId, IReadOnlyList<Grant> grants)
        => grants.Any(grant => !grant.Revoked
            && string.Equals(grant.WorkspaceId, caller.BrainId, StringComparison.Ordinal)
            && string.Equals(grant.AppId, caller.AppId, StringComparison.Ordinal)
            && string.Equals(grant.SemanticTypeId, semanticTypeId, StringComparison.Ordinal)
            && ModeSatisfied(grant, caller));

    private static bool ModeSatisfied(Grant grant, CallerContext caller) => grant.Mode switch
    {
        GrantMode.Always or GrantMode.Once => true,
        GrantMode.ThisChat => caller.ConversationId is { Length: > 0 }
            && string.Equals(grant.ConversationId, caller.ConversationId, StringComparison.Ordinal),
        _ => false,
    };
}
