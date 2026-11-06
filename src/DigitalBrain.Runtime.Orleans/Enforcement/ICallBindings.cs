using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Runtime.Orleans.Enforcement;

/// <summary>
/// The connection-binding seam of the call filter (app calls across brains).
/// Whether an app call into another brain's neuron is admitted through a binding recorded when the app was shared.
/// </summary>
public interface ICallBindings
{
    ValueTask<bool> IsBound(CallRequest request, string callerScope, CancellationToken cancellationToken = default);
}
