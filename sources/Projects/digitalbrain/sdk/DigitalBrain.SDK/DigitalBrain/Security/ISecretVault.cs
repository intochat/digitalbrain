namespace DigitalBrain.SDK.DigitalBrain.Security;

/// <summary>
/// Handles secure, encrypted storage and retrieval of sensitive credentials,
/// separating plain settings from protected vault records.
/// </summary>
public interface ISecretVault
{
    void StoreSecret(string key, string secret);
    string GetEncryptedSecret(string key);
    string DecryptSecret(string key);
    
    Task StoreSecretAsync(string key, string secret, CancellationToken ct = default);
    Task<string> GetEncryptedSecretAsync(string key, CancellationToken ct = default);
    Task<string> DecryptSecretAsync(string key, CancellationToken ct = default);
}
