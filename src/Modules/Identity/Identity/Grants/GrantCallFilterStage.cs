using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Identity.Grants;

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

        return GrantRules.Evaluate(request, member, grants);
    }
}
