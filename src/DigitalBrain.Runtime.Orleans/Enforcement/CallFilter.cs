using DigitalBrain.Brains;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Runtime.Orleans.Capabilities;

namespace DigitalBrain.Runtime.Orleans.Enforcement;

/// <summary>
/// The single enforcement point for <see cref="CallRequest"/>. It validates that the caller carries a trusted-edge stamp,
/// then lets registered stages decide.
/// </summary>
public sealed class CallFilter(IEnumerable<ICallFilterStage> stages, ICallBindings? bindings = null) : ICallFilter
{
    private readonly IReadOnlyList<ICallFilterStage> _stages = [.. stages.OrderBy(stage => stage.Order)];

    public async ValueTask<CallDecision> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RuntimeCallerContext.TryGet(out var caller) || caller is null)
        {
            return CallDecision.Deny(CallDenial.NotAuthenticated, "The request carries no principal.");
        }

        if (string.IsNullOrWhiteSpace(caller.PrincipalId))
        {
            return CallDecision.Deny(CallDenial.NotAuthenticated, "The request carries no principal.");
        }

        if (!RuntimeCallerContext.IsTrusted(caller))
        {
            return CallDecision.Deny(CallDenial.UntrustedCaller, "The principal was not stamped at a trusted edge.");
        }

        var callerScope = BrainScope.Create(caller.AccountId, caller.BrainId).Id;
        if (request.EnforceTarget && caller.Kind != CallerKind.Platform && !request.PublicOperation
            && request.TargetScope != callerScope && !await BoundConnection(request, caller, callerScope, cancellationToken).ConfigureAwait(false))
        {
            return CallDecision.Deny(CallDenial.OutsideWorkspace, "The target operation is not available in the selected brain.");
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

    private async ValueTask<bool> BoundConnection(CallRequest request, CapabilityCaller caller, string callerScope, CancellationToken cancellationToken)
    {
        if (bindings is null || caller.Kind != CallerKind.App || caller.AppId is not { Length: > 0 }) { return false; }
        return await bindings.IsBound(request, callerScope, cancellationToken).ConfigureAwait(false);
    }
}
