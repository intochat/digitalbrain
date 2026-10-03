using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Platform.Contracts.Auth;

// Server-side token operations. Never exposed to script contract discovery.
public interface IOAuthCredentials
{
    Task<SecretRef> StoreAccessToken(string provider, string owner, string value, CancellationToken ct);
    Task<SecretRef> StoreRefreshToken(string provider, string owner, string value, CancellationToken ct);
    Task<string> ResolveAccessToken(string provider, SecretRef reference, CancellationToken ct);
    Task<string> ResolveRefreshToken(string provider, SecretRef reference, CancellationToken ct);
}
