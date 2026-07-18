namespace DigitalBrain.Abstractions.Bundles;

// The bundle identities the substrate ships with. Each maps to one IBundle the Kernel can install.
public static class WellKnownBundleIds
{
    public const string Marketplace = "digitalbrain/marketplace";
    public const string Ino = "digitalbrain/ino";
    public const string Awesome = "digitalbrain/awesome";
    public const string AiLlm = "digitalbrain/ai.llm";
    public const string AiSearch = "digitalbrain/ai.search";
    public const string MicrosoftAspire = "digitalbrain/microsoft.aspire";
    public const string MicrosoftRoslyn = "digitalbrain/microsoft.roslyn";
    public const string WindowsFileSystem = "digitalbrain/windows.filesystem";
    public const string XaiGrok = "digitalbrain/xai.grok";

    // Opt-in connector: it needs a PostgreSQL resource, so it is installed only when configuration names
    // it, not by KernelBundleOptions.Default().
    public const string DataPostgres = "digitalbrain/data.postgres";

    // Opt-in connector: it owns user OAuth and secret storage, so callers install it explicitly.
    public const string AuthGoogle = "digitalbrain/auth.google";

    public static string Canonicalize(string value)
    {
        var token = value.Trim();
        return token.ToLowerInvariant() switch
        {
            "marketplace" => Marketplace,
            "ino" => Ino,
            "awesome" => Awesome,
            "ai.llm" or "llm" => AiLlm,
            "ai.search" or "search" => AiSearch,
            "microsoft.aspire" or "aspire" => MicrosoftAspire,
            "microsoft.roslyn" or "roslyn" => MicrosoftRoslyn,
            "windows.filesystem" or "filesystem" or "file-system" => WindowsFileSystem,
            "xai.grok" or "grok" => XaiGrok,
            "data.postgres" => DataPostgres,
            "auth.google" => AuthGoogle,
            _ => token
        };
    }
}
