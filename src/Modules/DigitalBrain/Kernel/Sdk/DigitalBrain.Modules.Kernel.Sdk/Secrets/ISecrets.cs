using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Sdk.Secrets;

// Keep the existing Orleans identity so stored credentials remain addressable.
[Orleans.Metadata.DefaultGrainType("vault")]
[Alias("vault")]
[PlatformOnly]
public interface ISecrets : INeuron
{
    Task<SecretRef> Set(CallerContext caller, string name, string label, string value, CancellationToken cancellationToken = default);

    Task Remove(CallerContext caller, string name, CancellationToken cancellationToken = default);

    Task<string> Resolve(CallerContext caller, SecretRef secret, CancellationToken cancellationToken = default);
}
