using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Runtime.Orleans.Enforcement;

/// <summary>
/// The enforcement filter for incoming neuron calls.
/// </summary>
public interface ICallFilter
{
    ValueTask<CallDecision> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken = default);
}
