using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Platform.Secrets;

public sealed class MasterKeyWrapper : IKeyWrapper
{
    private const string Prefix = "mk3:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public MasterKeyWrapper(IConfiguration configuration)
    {
        var masterKey = configuration["DigitalBrain:MasterKey"];
        if (string.IsNullOrWhiteSpace(masterKey))
        { throw new InvalidOperationException("Configure DigitalBrain:MasterKey via the DigitalBrain__MasterKey environment secret before starting IntoChat."); }
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(masterKey), 32,
            info: "DigitalBrain.OwnerKeys.v3"u8.ToArray());
    }

    public string Wrap(byte[] key)
    {
        var payload = new byte[NonceSize + TagSize + key.Length];
        RandomNumberGenerator.Fill(payload.AsSpan(0, NonceSize));
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(payload.AsSpan(0, NonceSize), key, payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize));
        return Prefix + Convert.ToBase64String(payload);
    }

    public byte[] Unwrap(string wrapped)
    {
        if (!wrapped.StartsWith(Prefix, StringComparison.Ordinal))
        { throw new CryptographicException("Owner key is not wrapped by the configured master key (expected mk3:)."); }
        byte[] payload;
        try { payload = Convert.FromBase64String(wrapped[Prefix.Length..]); }
        catch (FormatException error) { throw new CryptographicException("Malformed master-key wrapped owner key.", error); }
        if (payload.Length < NonceSize + TagSize)
        { throw new CryptographicException("Truncated master-key wrapped owner key."); }
        var key = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), key);
        return key;
    }
}
