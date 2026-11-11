using DigitalBrain.Modules;

namespace DigitalBrain.Modules.Options;

public static class ModuleSettingsValidation
{
    // Public: the credential-name rule shared by settings validation and the architecture contract rules.
    public static bool IsCredentialName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Password", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ApiKey", StringComparison.OrdinalIgnoreCase) || name.EndsWith("PrivateKeyPem", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("AccessToken", StringComparison.OrdinalIgnoreCase) || name.EndsWith("RefreshToken", StringComparison.OrdinalIgnoreCase);
    }
}
