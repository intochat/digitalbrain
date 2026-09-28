using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Sdk.Secrets;

// Infrastructure-only interface, intentionally not an INeuron and not exported by the UI registry.
[Orleans.Metadata.DefaultGrainType("vault")]
public interface ISecretKeyMigration : IGrainWithStringKey
{
    Task<bool> EnsurePortable();
}

// The host must configure a shared key ring before selecting this wrapper.
public sealed class DataProtectionKeyWrapper(IDataProtectionProvider provider) : IKeyWrapper
{
    private readonly IDataProtector _protector = provider.CreateProtector("DigitalBrain.OwnerKeys.v2");
    private const string Prefix = "dp2:";
    public string Wrap(byte[] key) => Prefix + Convert.ToBase64String(_protector.Protect(key));
    public byte[] Unwrap(string wrapped)
    {
        if (wrapped.StartsWith(Prefix, StringComparison.Ordinal))
        { return _protector.Unprotect(Convert.FromBase64String(wrapped[Prefix.Length..])); }
        // Read-only migration adapters. Never create another DPAPI or fake-vault record.
        if (wrapped.StartsWith("kv1:", StringComparison.Ordinal))
        { return Convert.FromBase64String(wrapped[4..]); }
        return new DpapiKeyWrapper().Unwrap(wrapped);
    }
    public static bool NeedsMigration(string wrapped) => !wrapped.StartsWith(Prefix, StringComparison.Ordinal);
}
