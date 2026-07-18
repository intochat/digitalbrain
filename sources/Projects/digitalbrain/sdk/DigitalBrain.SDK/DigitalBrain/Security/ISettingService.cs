namespace DigitalBrain.SDK.DigitalBrain.Security;

/// <summary>
/// Handles plaintext storage and retrieval of standard user/global configuration variables.
/// </summary>
public interface ISettingService
{
    void StoreSetting(string key, string value);
    string GetSetting(string key);
    
    Task StoreSettingAsync(string key, string value, CancellationToken ct = default);
    Task<string> GetSettingAsync(string key, CancellationToken ct = default);
}
