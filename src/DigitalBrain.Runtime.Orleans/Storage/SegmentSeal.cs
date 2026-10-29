using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Runtime.Orleans;

// AES-256-GCM with the caller's key; the sealed form is nonce, ciphertext, tag. The context (segment id and
// whether this is data or its manifest) is authenticated, so a sealed blob cannot be replayed as another one.
internal static class SegmentSeal
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static byte[] Close(byte[] key, string context, byte[] plaintext)
    {
        RequireKey(key);
        ArgumentNullException.ThrowIfNull(plaintext);
        var sealedBytes = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = sealedBytes.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, sealedBytes.AsSpan(NonceSize, plaintext.Length), sealedBytes.AsSpan(NonceSize + plaintext.Length), Encoding.UTF8.GetBytes(context));
        return sealedBytes;
    }

    public static byte[] Open(byte[] key, string context, byte[] sealedBytes)
    {
        RequireKey(key);
        ArgumentNullException.ThrowIfNull(sealedBytes);
        if (sealedBytes.Length < NonceSize + TagSize) { throw new AuthenticationTagMismatchException("Sealed bytes are shorter than a nonce and a tag."); }
        var plain = new byte[sealedBytes.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(sealedBytes.AsSpan(0, NonceSize), sealedBytes.AsSpan(NonceSize, plain.Length), sealedBytes.AsSpan(NonceSize + plain.Length), plain, Encoding.UTF8.GetBytes(context));
        return plain;
    }

    private static void RequireKey(byte[] key)
    {
        if (key is not { Length: 32 }) { throw new ArgumentException("A mesh key is 32 bytes.", nameof(key)); }
    }
}
