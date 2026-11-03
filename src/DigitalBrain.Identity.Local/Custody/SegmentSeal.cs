using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Identity.Local.Custody;

/// <summary>
/// AES-256-GCM authenticated encryption over a 32-byte key; the sealed representation is nonce (12 bytes),
/// ciphertext and tag (16 bytes), with the context bound as associated data.
/// </summary>
public static class SegmentSeal
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>Encrypts and authenticates <paramref name="plaintext"/> under <paramref name="key"/> and <paramref name="context"/>.</summary>
    public static byte[] Close(byte[] key, string context, byte[] plaintext)
    {
        RequireKey(key);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plaintext);
        var sealedBytes = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = sealedBytes.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, sealedBytes.AsSpan(NonceSize, plaintext.Length), sealedBytes.AsSpan(NonceSize + plaintext.Length), Encoding.UTF8.GetBytes(context));
        return sealedBytes;
    }

    /// <summary>Decrypts and verifies <paramref name="sealedBytes"/> under <paramref name="key"/> and <paramref name="context"/>.</summary>
    public static byte[] Open(byte[] key, string context, byte[] sealedBytes)
    {
        RequireKey(key);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sealedBytes);
        if (sealedBytes.Length < NonceSize + TagSize) { throw new AuthenticationTagMismatchException("Sealed bytes are shorter than a nonce and a tag."); }
        var plain = new byte[sealedBytes.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(sealedBytes.AsSpan(0, NonceSize), sealedBytes.AsSpan(NonceSize, plain.Length), sealedBytes.AsSpan(NonceSize + plain.Length), plain, Encoding.UTF8.GetBytes(context));
        return plain;
    }

    private static void RequireKey(byte[] key)
    {
        if (key is not { Length: 32 }) { throw new ArgumentException("A key must be 32 bytes.", nameof(key)); }
    }
}
