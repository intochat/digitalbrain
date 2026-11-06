using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Runtime.Orleans.Enforcement;

/// <summary>
/// A stage of the call filter, the single enforcement point for <see cref="CallRequest"/>.
/// </summary>
public interface ICallFilterStage
{
    int Order => 0;

    ValueTask<CallDecision?> EvaluateAsync(CallRequest request, CancellationToken cancellationToken = default);
}
