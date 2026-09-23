namespace DigitalBrain.MyData;

// Hosted key-management adapter (Azure Key Vault or equivalent). The fake keeps hosted
// composition and tests deterministic without reaching a real vault.
public interface IKeyVault
{
    string Wrap(byte[] key);

    byte[] Unwrap(string wrapped);
}

public sealed class KeyVaultKeyWrapper(IKeyVault vault) : IKeyWrapper
{
    public string Wrap(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return vault.Wrap(key);
    }

    public byte[] Unwrap(string wrapped)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wrapped);
        return vault.Unwrap(wrapped);
    }
}

public sealed class FakeKeyVault : IKeyVault
{
    private const string Prefix = "kv1:";

    public string Wrap(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Prefix + Convert.ToBase64String(key);
    }

    public byte[] Unwrap(string wrapped)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wrapped);
        return Convert.FromBase64String(wrapped.StartsWith(Prefix, StringComparison.Ordinal) ? wrapped[Prefix.Length..] : wrapped);
    }
}
