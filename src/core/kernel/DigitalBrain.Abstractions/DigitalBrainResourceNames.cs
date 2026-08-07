namespace DigitalBrain.Abstractions;

public static class DigitalBrainResourceNames
{
    public const string DefaultBrainName = "brain";

    // Connection string alias injected on the silo via WithReference(journal, name).
    public const string JournalConnectionName = "journal";

    public const string ModulesConfigurationKey = "DigitalBrain:Modules";
    public const string OwnerConfigurationKey = "DigitalBrain:Owner";
    public const string StateProtectionKeyConfigurationKey = "DigitalBrain:Security:StateProtectionKey";

    public static string Storage(string brainName = DefaultBrainName)
        => $"{RequireBrainName(brainName)}-storage";

    public static string Clustering(string brainName = DefaultBrainName)
        => $"{RequireBrainName(brainName)}-clustering";

    public static string Reminders(string brainName = DefaultBrainName)
        => $"{RequireBrainName(brainName)}-reminders";

    public static string JournalResource(string brainName = DefaultBrainName)
        => $"{RequireBrainName(brainName)}-journal";

    private static string RequireBrainName(string brainName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainName);
        return brainName;
    }
}
