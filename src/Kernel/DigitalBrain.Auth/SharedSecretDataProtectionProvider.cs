using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Auth;

// Deterministic protector so Kernel and MCP unprotect the same auth cookie
// when they share DigitalBrain:Security:StateProtectionKey.
public sealed class SharedSecretDataProtectionProvider : IDataProtectionProvider
{
    private readonly byte[] _rootKey;

    public SharedSecretDataProtectionProvider(string material)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(material);
        _rootKey = SHA256.HashData(Encoding.UTF8.GetBytes("DigitalBrain.Auth.DP|" + material.Trim()));
    }

    public IDataProtector CreateProtector(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return new PurposeProtector(_rootKey, purpose);
    }

    private sealed class PurposeProtector : IDataProtector
    {
        private readonly byte[] _key;
        private readonly string _purpose;

        public PurposeProtector(byte[] rootKey, string purpose)
        {
            _purpose = purpose;
            _key = SHA256.HashData(rootKey.Concat(Encoding.UTF8.GetBytes(purpose)).ToArray());
        }

        public IDataProtector CreateProtector(string purpose)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
            return new PurposeProtector(_key, _purpose + "/" + purpose);
        }

        public byte[] Protect(byte[] plaintext)
        {
            ArgumentNullException.ThrowIfNull(plaintext);

            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(_key, tag.Length);
            aes.Encrypt(nonce, plaintext, cipher, tag);

            var payload = new byte[nonce.Length + tag.Length + cipher.Length];
            Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
            return payload;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            ArgumentNullException.ThrowIfNull(protectedData);
            if (protectedData.Length < 12 + 16)
            {
                throw new CryptographicException("Auth cookie payload is truncated.");
            }

            var nonce = protectedData.AsSpan(0, 12);
            var tag = protectedData.AsSpan(12, 16);
            var cipher = protectedData.AsSpan(28);
            var plaintext = new byte[cipher.Length];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, cipher, tag, plaintext);
            return plaintext;
        }
    }
}
