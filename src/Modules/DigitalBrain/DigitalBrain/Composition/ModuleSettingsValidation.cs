namespace DigitalBrain.Core;

public static class ModuleSettingsValidation
{
    public static void ValidatePublicSettings(IReadOnlyList<ModuleDefinition> modules)
    {
        foreach (var key in modules.SelectMany(m => m.Configuration.Keys))
        {
            if (string.IsNullOrWhiteSpace(key) || key.Contains("__", StringComparison.Ordinal)
                || key.StartsWith("Orleans:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Testing:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Modules:", StringComparison.OrdinalIgnoreCase))
                { throw new ArgumentException("Module configuration cannot override host-owned settings.", nameof(modules)); }
            if (key.Contains("Secret", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Password", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("ApiKey", StringComparison.OrdinalIgnoreCase) || key.EndsWith("PrivateKeyPem", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("AccessToken", StringComparison.OrdinalIgnoreCase) || key.EndsWith("RefreshToken", StringComparison.OrdinalIgnoreCase))
                { throw new ArgumentException("Credentials require private configuration transport.", nameof(modules)); }
        }
    }
}
