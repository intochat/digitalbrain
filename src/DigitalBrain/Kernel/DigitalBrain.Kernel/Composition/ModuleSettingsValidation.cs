namespace DigitalBrain.Kernel;

public static class ModuleSettingsValidation
{
    public static void ValidatePublicSettings(IReadOnlyList<ModuleDefinition> modules)
    {
        foreach (var key in modules.SelectMany(m => m.Configuration.Keys))
        {
            if (ModuleOptionsSerialization.IsOptionsKey(key)) { continue; }
            if (string.IsNullOrWhiteSpace(key) || key.Contains("__", StringComparison.Ordinal)
                || key.StartsWith("Orleans:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Testing:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Modules:", StringComparison.OrdinalIgnoreCase))
            { throw new ArgumentException("Module configuration cannot override host-owned settings.", nameof(modules)); }
            if (IsCredentialName(key))
            { throw new ArgumentException("Credentials require private configuration transport.", nameof(modules)); }
        }
    }

    internal static bool IsCredentialName(string name) =>
        name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Password", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("ApiKey", StringComparison.OrdinalIgnoreCase) || name.EndsWith("PrivateKeyPem", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("AccessToken", StringComparison.OrdinalIgnoreCase) || name.EndsWith("RefreshToken", StringComparison.OrdinalIgnoreCase);
}
