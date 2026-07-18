using System.Security.Cryptography;
using TripRadar.Server.Comms.Core.Contracts.Encryptions;

namespace TripRadar.Server.Comms.Core.Encryptions;

public class AesCryptographer : ICryptographer
{
    private static readonly byte[] Salt = "TripRadarSalt"u8.ToArray();
    private readonly byte[] _iv;
    private readonly byte[] _key;

    public AesCryptographer(string name, string key)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Encryption key cannot be null or empty", nameof(key));
        }

        var keyMaterial = Rfc2898DeriveBytes.Pbkdf2(key, Salt, 10000, HashAlgorithmName.SHA256, 48);
        _key = keyMaterial[..32];
        _iv = keyMaterial[32..48];
    }

    public string Name { get; }

    public byte[]? Encrypt(byte[]? data)
    {
        if (data == null || data.Length == 0)
        {
            return data;
        }

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var memoryStream = new MemoryStream();
        using var cryptoStream = new CryptoStream(
            memoryStream,
            aes.CreateEncryptor(),
            CryptoStreamMode.Write);

        cryptoStream.Write(data, 0, data.Length);
        cryptoStream.FlushFinalBlock();

        return memoryStream.ToArray();
    }

    public byte[]? Decrypt(byte[]? encryptedData)
    {
        if (encryptedData == null || encryptedData.Length == 0)
        {
            return encryptedData;
        }

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var memoryStream = new MemoryStream(encryptedData);
        using var cryptoStream = new CryptoStream(memoryStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var resultStream = new MemoryStream();

        cryptoStream.CopyTo(resultStream);
        return resultStream.ToArray();
    }
}
