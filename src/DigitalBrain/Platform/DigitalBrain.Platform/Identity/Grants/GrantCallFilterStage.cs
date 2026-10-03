using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;

namespace DigitalBrain.Platform.Identity.Grants;

internal sealed class GrantCallFilterStage(IGrantPolicySource source) : ICallFilterStage
{
    public ValueTask<CallDecision?> EvaluateAsync(CallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return source.AuthorizeAsync(request, cancellationToken);
    }
}
