namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

/// <summary>
/// An Orleans-native grain that securely stores encrypted GitHub authentication tokens.
/// Keyed by user identifier or project name to ensure "do it only one time" persistence.
/// </summary>
public interface IGitHubCredentialStore : IGrainWithStringKey
{
    Task SetEncryptedTokenAsync(byte[] encryptedToken);
    Task<byte[]?> GetEncryptedTokenAsync();
    Task ClearTokenAsync();
}
