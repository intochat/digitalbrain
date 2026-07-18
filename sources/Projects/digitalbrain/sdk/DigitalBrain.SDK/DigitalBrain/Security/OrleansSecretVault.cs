using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.Security;

public sealed class OrleansSecretVault : ISecretVault
{
    private readonly IGrainFactory _grainFactory;
    private readonly IKernelUser _kernelUser;
    
    // Key used for cross-platform AES fallback (AES-256)
    private static readonly byte[] FallbackAesKey = Encoding.UTF8.GetBytes("DigitalBrainSuperSecureKey1234567890!");

    public OrleansSecretVault(IGrainFactory grainFactory, IKernelUser kernelUser)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _kernelUser = kernelUser ?? throw new ArgumentNullException(nameof(kernelUser));
    }

    private string GetActiveScope()
    {
        return _kernelUser.IsAuthenticated ? _kernelUser.Username : "global";
    }

    private ICallNeuronTarget GetStoreGrain()
    {
        var grainId = GrainId.Create(
            GrainType.Create("DigitalBrain.Kernel.Settings.SettingsStore"), 
            "global");
        return _grainFactory.GetGrain<ICallNeuronTarget>(grainId);
    }

    public void StoreSecret(string key, string secret)
    {
        StoreSecretAsync(key, secret).GetAwaiter().GetResult();
    }

    public string GetEncryptedSecret(string key)
    {
        return GetEncryptedSecretAsync(key).GetAwaiter().GetResult();
    }

    public string DecryptSecret(string key)
    {
        return DecryptSecretAsync(key).GetAwaiter().GetResult();
    }

    public async Task StoreSecretAsync(string key, string secret, CancellationToken ct = default)
    {
        if (secret == null) throw new ArgumentNullException(nameof(secret));

        // 1. Perform platform-specific encryption (Windows DPAPI vs AES Fallback)
        byte[] encryptedBytes;
        if (OperatingSystem.IsWindows())
        {
            encryptedBytes = WindowsDpapiEncrypt(secret);
        }
        else
        {
            encryptedBytes = CrossPlatformAesEncrypt(secret);
        }

        // 2. Base64 encode and prefix with "ENC:" to meet BDD expectations
        var base64 = Convert.ToBase64String(encryptedBytes);
        var cipherText = $"ENC:{base64}";

        // 3. Persist to Kernel via "set-private" command
        var store = GetStoreGrain();
        var scope = GetActiveScope();
        var prompt = $"set-private {scope}:{key}={cipherText}";
        
        var result = await store.AskAsync(prompt);
        if (result != "ok")
        {
            throw new InvalidOperationException($"Failed to store private secret '{key}'. Kernel returned: '{result}'");
        }
    }

    public async Task<string> GetEncryptedSecretAsync(string key, CancellationToken ct = default)
    {
        var store = GetStoreGrain();
        var scope = GetActiveScope();
        var prompt = $"get-private {scope}:{key}";
        
        var result = await store.AskAsync(prompt);
        if (string.IsNullOrEmpty(result))
        {
            throw new KeyNotFoundException($"The secret '{key}' in scope '{scope}' was not found.");
        }

        return result;
    }

    public async Task<string> DecryptSecretAsync(string key, CancellationToken ct = default)
    {
        // 1. Fetch "ENC:..." ciphertext
        var cipherText = await GetEncryptedSecretAsync(key, ct);
        
        if (!cipherText.StartsWith("ENC:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stored secret is not correctly encrypted (missing 'ENC:' prefix).");
        }

        // 2. Strip "ENC:" prefix and decode Base64
        var base64 = cipherText.Substring("ENC:".Length);
        byte[] encryptedBytes;
        try
        {
            encryptedBytes = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Failed to decode base64 ciphertext.", ex);
        }

        // 3. Platform-specific decryption (Windows DPAPI vs AES Fallback)
        string decrypted;
        if (OperatingSystem.IsWindows())
        {
            decrypted = WindowsDpapiDecrypt(encryptedBytes);
        }
        else
        {
            decrypted = CrossPlatformAesDecrypt(encryptedBytes);
        }

        return decrypted;
    }

    [SupportedOSPlatform("windows")]
    private static byte[] WindowsDpapiEncrypt(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        return ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    [SupportedOSPlatform("windows")]
    private static string WindowsDpapiDecrypt(byte[] ciphertext)
    {
        var decryptedBytes = ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    private static byte[] CrossPlatformAesEncrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = FallbackAesKey;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor(aes.Key, iv);
        using var ms = new MemoryStream();
        
        // Write standard IV header
        ms.Write(iv, 0, iv.Length);

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plaintext);
        }

        return ms.ToArray();
    }

    private static string CrossPlatformAesDecrypt(byte[] ciphertext)
    {
        if (ciphertext.Length < 16) throw new CryptographicException("Ciphertext is too short.");

        using var aes = Aes.Create();
        aes.Key = FallbackAesKey;

        // Parse IV from header
        var iv = new byte[16];
        Buffer.BlockCopy(ciphertext, 0, iv, 0, 16);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(ciphertext, 16, ciphertext.Length - 16);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);

        return sr.ReadToEnd();
    }
}
