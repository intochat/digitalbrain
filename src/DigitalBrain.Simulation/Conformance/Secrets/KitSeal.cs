using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Testing.Conformance.Secrets;

// AES-256-GCM under a random per-instance key; the context is the associated data. Cipher = base64(nonce | tag | ciphertext).
internal sealed class KitSeal
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string KeyId { get; } = $"memory-{Guid.NewGuid():N}";

    public SealedSecret Seal(string context, ReadOnlySpan<byte> plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        var output = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(context));
        return new SealedSecret { Cipher = Convert.ToBase64String(output), KeyId = KeyId };
    }

    // Null for another key, a malformed cipher, another context or tampering.
    public byte[]? Open(string context, SealedSecret sealedSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        ArgumentNullException.ThrowIfNull(sealedSecret);
        if (!string.Equals(sealedSecret.KeyId, KeyId, StringComparison.Ordinal)) { return null; }
        try
        {
            var input = Convert.FromBase64String(sealedSecret.Cipher ?? "");
            if (input.Length < NonceSize + TagSize) { return null; }
            var plaintext = new byte[input.Length - NonceSize - TagSize];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize + TagSize), input.AsSpan(NonceSize, TagSize), plaintext, Encoding.UTF8.GetBytes(context));
            return plaintext;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
