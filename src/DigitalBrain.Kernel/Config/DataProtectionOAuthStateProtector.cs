using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using Microsoft.AspNetCore.DataProtection;
namespace DigitalBrain.Kernel.Config;

internal sealed class DataProtectionOAuthStateProtector : IOAuthStateProtector
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);
    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeSpan _lifetime;
    public DataProtectionOAuthStateProtector(IDataProtectionProvider provider)
        : this(provider, DefaultLifetime) { }
    public DataProtectionOAuthStateProtector(IDataProtectionProvider provider, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        _protector = provider.CreateProtector("DigitalBrain.OAuth.State.v3").ToTimeLimitedDataProtector();
        _lifetime = lifetime;
    }
    public string Protect(NeuronId owner)
    {
        if (string.IsNullOrWhiteSpace(owner.Value) || owner.Value.Length > 256)
            throw new ArgumentException("A bounded OAuth owner is required.", nameof(owner));
        return _protector.Protect(JsonSerializer.Serialize(new StatePayload(owner.Value, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))), _lifetime);
    }
    public bool TryUnprotect(string state, out NeuronId owner)
    {
        owner = new NeuronId(string.Empty);
        if (string.IsNullOrWhiteSpace(state) || state.Length > 4096) return false;
        try
        {
            var payload = JsonSerializer.Deserialize<StatePayload>(_protector.Unprotect(state));
            if (payload is null || string.IsNullOrWhiteSpace(payload.Owner) || payload.Owner.Length > 256 ||
                string.IsNullOrWhiteSpace(payload.Nonce))
                return false;
            owner = new NeuronId(payload.Owner);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
    private sealed record StatePayload(string Owner, string Nonce);
}
