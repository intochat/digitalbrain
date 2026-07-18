using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.SDK.DigitalBrain.Identity.Identity;

public static class AesEncryption
{
    // A fixed key for demonstration / development encryption.
    // 32 bytes for AES-256
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("DigitalBrainSuperSecureKey1234567890!");

    public static string Encrypt(string plainText)
    {
        if (plainText == null) throw new ArgumentNullException(nameof(plainText));

        using var aes = Aes.Create();
        aes.Key = Key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor(aes.Key, iv);
        using var ms = new MemoryStream();
        
        // Write IV first
        ms.Write(iv, 0, iv.Length);

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    public static string Decrypt(string cipherText)
    {
        if (cipherText == null) throw new ArgumentNullException(nameof(cipherText));

        var fullBytes = Convert.FromBase64String(cipherText);
        if (fullBytes.Length < 16) throw new InvalidOperationException("Invalid cipher bytes.");

        using var aes = Aes.Create();
        aes.Key = Key;

        var iv = new byte[16];
        Buffer.BlockCopy(fullBytes, 0, iv, 0, 16);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(fullBytes, 16, fullBytes.Length - 16);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);

        return sr.ReadToEnd();
    }
}
