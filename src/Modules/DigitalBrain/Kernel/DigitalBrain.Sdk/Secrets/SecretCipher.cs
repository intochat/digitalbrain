using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Sdk.Secrets;

internal static class SecretCipher
{
    internal const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    internal static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySize);

    internal static string Seal(byte[] key, string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        var combined = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, NonceSize);
        cipher.CopyTo(combined, NonceSize + TagSize);
        return Convert.ToBase64String(combined);
    }

    internal static string Open(byte[] key, string sealedText)
    {
        var sealedBytes = Convert.FromBase64String(sealedText);
        var nonce = sealedBytes.AsSpan(0, NonceSize);
        var tag = sealedBytes.AsSpan(NonceSize, TagSize);
        var cipher = sealedBytes.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
