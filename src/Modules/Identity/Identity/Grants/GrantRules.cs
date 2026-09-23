using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Identity.Grants;

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
        if (member is not null && !string.Equals(member.WorkspaceId, caller.WorkspaceId, StringComparison.Ordinal))
        {
            return CallDecision.Deny(CallDenial.OutsideWorkspace, "The principal is not a member of this workspace.");
        }

        if (caller.Kind != CallerKind.App || request.SemanticTypeIds.Count == 0)
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

    private static bool IsGranted(CallerContext caller, string semanticTypeId, IReadOnlyList<Grant> grants)
        => grants.Any(grant => !grant.Revoked
            && string.Equals(grant.WorkspaceId, caller.WorkspaceId, StringComparison.Ordinal)
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
