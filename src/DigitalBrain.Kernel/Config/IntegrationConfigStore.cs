using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Kernel.Config;

internal sealed class IntegrationConfigStore(IDataProtectionProvider dpProvider, IIntegrationConfigBackingStore backing, ILogger<IntegrationConfigStore>? logger = null)
    : IIntegrationConfigStore
{
    private const string RootPurpose = "DigitalBrain.IntegrationConfig";

    private IDataProtector ValueProtector(string scope, string pack, string key)
        => dpProvider.CreateProtector(RootPurpose, scope, pack, key);

    public async Task SetAsync(string scope, string pack, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var encrypted = values.ToDictionary(
            kv => kv.Key,
            kv => ValueProtector(scope, pack, kv.Key).Protect(kv.Value));

        var blob = JsonSerializer.SerializeToUtf8Bytes(encrypted);
        await backing.SaveAsync(scope, pack, blob, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string scope, string pack, CancellationToken cancellationToken = default)
    {
        var blob = await backing.LoadAsync(scope, pack, cancellationToken);
        if (blob is null)
        {
            return new Dictionary<string, string>();
        }

        var encrypted = JsonSerializer.Deserialize<Dictionary<string, string>>(blob) ?? [];

        var result = new Dictionary<string, string>(encrypted.Count);
        foreach (var (key, ciphertext) in encrypted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                result[key] = ValueProtector(scope, pack, key).Unprotect(ciphertext);
            }
            catch (CryptographicException ex)
            {
                logger?.LogWarning(ex, "Could not decrypt config value '{Key}' for pack {Pack} (scope {Scope}); skipping.", key, pack, scope);
            }
        }

        return result;
    }
}
