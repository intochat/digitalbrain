using System.Security.Cryptography;
using DigitalBrain.Identity.Local.Custody;

namespace DigitalBrain.Identity.Local.Vault;

/// <summary>Wraps and unwraps per-owner vault keys under the host master key (`mk3:` envelope).</summary>
public sealed class MasterKeyWrapper
{
    private const string Prefix = "mk3:";
    private const string CustodyAccount = "local";
    private const string ContextName = "DigitalBrain.OwnerKeys.v3";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IMasterKeyCustody? _custody;
    private readonly byte[]? _key;

    // HKDF domain separator. Never rename: changing it makes every sealed owner key underivable.
    internal static ReadOnlySpan<byte> OwnerKeysContext => "DigitalBrain.OwnerKeys.v3"u8;

    /// <summary>Creates a wrapper backed by <paramref name="custody"/>.</summary>
    public MasterKeyWrapper(IMasterKeyCustody custody)
    {
        ArgumentNullException.ThrowIfNull(custody);
        _custody = custody;
    }

    /// <summary>Creates a wrapper from a raw <paramref name="masterKey"/>.</summary>
    public MasterKeyWrapper(ReadOnlySpan<byte> masterKey)
    {
        if (masterKey.IsEmpty)
        {
            throw new ArgumentException("The master key cannot be empty.", nameof(masterKey));
        }

        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey.ToArray(), 32, info: OwnerKeysContext.ToArray());
    }

    /// <summary>Wraps <paramref name="key"/> into an <c>mk3:</c>-prefixed base64 envelope.</summary>
    public string Wrap(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_custody is not null)
        {
            return Prefix + Convert.ToBase64String(_custody.Wrap(CustodyAccount, ContextName, key));
        }

        var payload = new byte[NonceSize + TagSize + key.Length];
        RandomNumberGenerator.Fill(payload.AsSpan(0, NonceSize));
        using var aes = new AesGcm(_key!, TagSize);
        aes.Encrypt(payload.AsSpan(0, NonceSize), key, payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize));
        return Prefix + Convert.ToBase64String(payload);
    }

    /// <summary>Unwraps an <c>mk3:</c>-prefixed base64 envelope back into the raw owner key.</summary>
    public byte[] Unwrap(string wrapped)
    {
        ArgumentNullException.ThrowIfNull(wrapped);
        if (!wrapped.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Owner key is not wrapped by the configured master key (expected mk3:).");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(wrapped[Prefix.Length..]);
        }
        catch (FormatException error)
        {
            throw new CryptographicException("Malformed master-key wrapped owner key.", error);
        }

        if (_custody is not null)
        {
            return _custody.Unwrap(CustodyAccount, ContextName, payload);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Truncated master-key wrapped owner key.");
        }

        var key = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key!, TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), key);
        return key;
    }
}
