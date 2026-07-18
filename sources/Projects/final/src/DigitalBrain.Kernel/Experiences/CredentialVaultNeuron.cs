using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Os.Infrastructure.Orleans;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Kernel.Experiences;

[GenerateSerializer]
public sealed record VaultState
{
    [Id(0)]
    public Dictionary<string, StoredToken> Tokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[GenerateSerializer]
public sealed record StoredToken(
    [property: Id(0)] byte[] EncryptedBlob,
    [property: Id(1)] string Provider,
    [property: Id(2)] string Scope,
    [property: Id(3)] DateTimeOffset StoredAt);

[GrainType("credential-vault")]
public sealed class CredentialVaultNeuron : Neuron
{
    private readonly IPersistentState<VaultState> _vaultState;

    public CredentialVaultNeuron(
        [PersistentState("credentialvault", "Default")] IPersistentState<VaultState> vaultState)
    {
        _vaultState = vaultState;
    }

    public async Task StoreAccessTokenAsync(string provider, string scope, string accessToken)
    {
        var brainKeyAccount = this.GetPrimaryKeyString();
        var perScopeKey = DerivePerScopeKey(brainKeyAccount, provider, scope);
        var encryptedBlob = Encrypt(accessToken, perScopeKey);
        var key = MakeScopeKey(provider, scope);
        _vaultState.State.Tokens[key] = new StoredToken(encryptedBlob, provider, scope, DateTimeOffset.UtcNow);
        await _vaultState.WriteStateAsync();
    }

    public async Task<string?> GetAccessTokenAsync(string provider, string scope)
    {
        var brainKeyAccount = this.GetPrimaryKeyString();
        var key = MakeScopeKey(provider, scope);
        if (!_vaultState.State.Tokens.TryGetValue(key, out var entry))
        {
            return null;
        }
        var perScopeKey = DerivePerScopeKey(brainKeyAccount, provider, scope);
        return Decrypt(entry.EncryptedBlob, perScopeKey);
    }

    private static string MakeScopeKey(string provider, string scope) => $"{provider}:{scope}";

    private static byte[] DerivePerScopeKey(string brainKeyAccount, string provider, string scope)
    {
        // per-(account,provider,scope) keying per plan (RFC/best practices for scope isolation; not XOR).
        // Deterministic derivation from brain grain key (account) + provider + scope.
        var material = Encoding.UTF8.GetBytes($"{brainKeyAccount}:{provider}:{scope}");
        using var sha = SHA256.Create();
        return sha.ComputeHash(material);
    }

    private static byte[] Encrypt(string plaintext, byte[] derivedKey)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return Array.Empty<byte>();
        }
        var nonce = new byte[12];
        var random = new SecureRandom();
        random.NextBytes(nonce);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(derivedKey), 128, nonce);
        cipher.Init(true, parameters);
        var outputSize = cipher.GetOutputSize(plaintextBytes.Length);
        var ciphertext = new byte[outputSize];
        int len = cipher.ProcessBytes(plaintextBytes, 0, plaintextBytes.Length, ciphertext, 0);
        cipher.DoFinal(ciphertext, len);
        var blob = new byte[nonce.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, blob, nonce.Length, ciphertext.Length);
        return blob;
    }

    private static string? Decrypt(byte[] encryptedBlob, byte[] derivedKey)
    {
        if (encryptedBlob == null || encryptedBlob.Length < 12)
        {
            return null;
        }
        var nonce = new byte[12];
        Buffer.BlockCopy(encryptedBlob, 0, nonce, 0, 12);
        var ciphertext = new byte[encryptedBlob.Length - 12];
        Buffer.BlockCopy(encryptedBlob, 12, ciphertext, 0, ciphertext.Length);
        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(derivedKey), 128, nonce);
        cipher.Init(false, parameters);
        var outputSize = cipher.GetOutputSize(ciphertext.Length);
        var plaintextBytes = new byte[outputSize];
        int len = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, plaintextBytes, 0);
        int finalLen = cipher.DoFinal(plaintextBytes, len);
        return Encoding.UTF8.GetString(plaintextBytes, 0, len + finalLen);
    }
}
