using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Core.Enforcement;

// The one enforcement point. It first validates that the caller carries a trusted-edge stamp,
// then lets each registered stage decide. Stages are ordered increments: grants now, allowances
// and limits later, then the broker gateway. The first non-null decision wins; an abstaining
// pipeline allows.
public sealed class CallFilter(IEnumerable<ICallFilterStage> stages) : ICallFilter
{
    private readonly IReadOnlyList<ICallFilterStage> _stages = [.. stages];

    public async ValueTask<CallDecision> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = request.Caller;
        if (string.IsNullOrWhiteSpace(caller.PrincipalId))
        {
            return CallDecision.Deny(CallDenial.NotAuthenticated, "The request carries no principal.");
        }

        if (!CallerContextStamper.IsTrusted(caller))
        {
            return CallDecision.Deny(CallDenial.UntrustedCaller, "The principal was not stamped at a trusted edge.");
        }

        if (CallerContextStamper.TryGet(out var ambient) && ambient is not null && ambient != caller)
        {
            return CallDecision.Deny(CallDenial.UntrustedCaller, "The principal does not match the trusted edge stamp.");
        }

        foreach (var stage in _stages)
        {
            var decision = await stage.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
            if (decision is not null)
            {
                return decision;
            }
        }

        return CallDecision.Allow();
    }
}
