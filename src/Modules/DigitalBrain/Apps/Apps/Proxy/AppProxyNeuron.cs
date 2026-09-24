using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Apps.Proxy;

// The single platform proxy every app call passes through. It re-stamps the caller as an app at the
// trusted proxy edge and asks the one call filter before any registered platform handler runs.
[GrainType("app-proxy")]
internal sealed class AppProxyNeuron(ICallFilter filter) : Neuron, IAppProxy
{
    public async Task<AppProxyOutcome> Invoke(AppProxyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = new CallerContext
        {
            PrincipalId = request.PrincipalId,
            AccountId = request.AccountId,
            WorkspaceId = request.WorkspaceId,
            Kind = CallerKind.App,
            StampedBy = TrustedEdge.AppProxy,
            AppId = request.AppId,
        };
        if (!CallerContextStamper.IsTrusted(caller))
        {
            return new AppProxyOutcome { Allowed = false, Denial = nameof(CallDenial.UntrustedCaller), Explanation = "The app call is not a trusted proxy call." };
        }

        CallerContextStamper.Stamp(caller);
        var decision = await filter.AuthorizeAsync(new CallRequest
        {
            Caller = caller,
            TargetNeuron = request.TargetNeuron,
            Operation = request.Operation,
            SemanticTypeIds = request.SemanticTypeIds,
            EstimatedCompute = request.EstimatedCompute,
            HasSideEffects = request.HasSideEffects,
        });
        if (!decision.Allowed)
        {
            return new AppProxyOutcome { Allowed = false, Denial = decision.Denial?.ToString(), Explanation = decision.Explanation };
        }

        foreach (var handler in ServiceProvider.GetServices<IAppOperationHandler>())
        {
            if (await handler.InvokeAsync(request))
            {
                return new AppProxyOutcome { Allowed = true, Explanation = "Handled by the platform." };
            }
        }
        return new AppProxyOutcome { Allowed = true, Explanation = "Allowed; no platform handler is registered for this operation." };
    }
}
