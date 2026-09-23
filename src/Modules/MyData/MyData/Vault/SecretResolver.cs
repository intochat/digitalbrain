using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;

namespace DigitalBrain.MyData;

// The only path to a secret value. It never returns the value to a read surface, and it enforces
// the caller policy before the vault grain is asked to decrypt.
public sealed class SecretResolver(IGrainFactory grains) : ISecretResolver
{
    public async ValueTask<string> ResolveAsync(SecretRef secret, CallerContext caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        SecretResolutionPolicy.EnsureAllowed(caller);
        var owner = VaultStore.OwnerOf(secret);
        if (string.IsNullOrEmpty(owner))
        {
            throw new SecretResolutionException("The secret reference does not name an owner vault.");
        }

        return await grains.GetGrain<IVault>(owner).ResolveSecret(caller, secret, cancellationToken);
    }
}
