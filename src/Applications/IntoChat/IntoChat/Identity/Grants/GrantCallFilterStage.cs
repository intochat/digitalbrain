using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

namespace IntoChat.Identity.Grants;

// P2's only stage: workspace membership plus grants. Allowances and limits arrive as further
// stages in P3 without touching this one.
internal sealed class GrantCallFilterStage(IGrantPolicySource source) : ICallFilterStage
{
    public async ValueTask<CallDecision?> EvaluateAsync(CallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = request.Caller;
        Member? member = caller.Kind is CallerKind.User or CallerKind.Assistant
            ? await source.FindMemberAsync(caller, cancellationToken).ConfigureAwait(false)
            : null;
        IReadOnlyList<Grant> grants = caller.Kind == CallerKind.App
            ? await source.ListGrantsAsync(caller, cancellationToken).ConfigureAwait(false)
            : [];

        var decision = GrantRules.Evaluate(request, member, grants);
        // A one-time grant is spent by the read it just authorized; an always grant is not.
        if (decision is { Allowed: true })
        {
            var spent = GrantRules.OnceToConsume(request, grants);
            if (spent.Count > 0)
            {
                await source.ConsumeOnceAsync(caller, spent, cancellationToken).ConfigureAwait(false);
            }
        }

        return decision;
    }
}
