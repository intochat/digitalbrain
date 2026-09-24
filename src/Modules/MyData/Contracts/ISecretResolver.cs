using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;

namespace DigitalBrain.MyData;

// Called only inside the neuron that makes the outbound call; the value never leaves that call.
public interface ISecretResolver
{
    ValueTask<string> ResolveAsync(SecretRef secret, CallerContext caller, CancellationToken cancellationToken = default);
}
