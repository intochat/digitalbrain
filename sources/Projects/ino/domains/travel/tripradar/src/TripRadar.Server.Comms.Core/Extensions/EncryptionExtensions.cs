using System.Security.Cryptography;
using System.Text;

namespace TripRadar.Server.Comms.Core.Extensions;

public static class EncryptionExtensions
{
    private const string EncryptionPrefix = "encv1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly Lock _sync = new();
    private static byte[]? _userDataKey;

    public static void Configure(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Encryption key must be configured before encrypting user data.");
        }

        var derivedKey = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        lock (_sync)
        {
            _userDataKey = derivedKey;
        }
    }

    public static string? EncryptString(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith(EncryptionPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        var key = GetRequiredKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);

        return EncryptionPrefix + Convert.ToBase64String(payload);
    }

    public static string DecryptString(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.StartsWith(EncryptionPrefix, StringComparison.Ordinal))
        {
            return DecryptEncryptedPayload(value);
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return value;
        }
    }

    private static string DecryptEncryptedPayload(string value)
    {
        var key = GetRequiredKey();
        var payload = Convert.FromBase64String(value[EncryptionPrefix.Length..]);

        if (payload.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Encrypted payload is invalid.");
        }

        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var ciphertext = payload[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] GetRequiredKey()
    {
        lock (_sync)
        {
            return _userDataKey is { Length: > 0 }
                ? _userDataKey
                : throw new InvalidOperationException("EncryptionExtensions.Configure must be called before encrypting or decrypting protected values.");
        }
    }
}
