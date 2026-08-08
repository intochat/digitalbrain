namespace DigitalBrain.Abstractions;

public static class DigitalBrainResourceNames
{
    public const string DefaultBrainName = "brain";

    public const string JournalConnectionName = "journal";

    public const string ModulesConfigurationKey = "DigitalBrain:Modules";
    public const string OwnerConfigurationKey = "DigitalBrain:Owner";
    public const string StateProtectionKeyConfigurationKey = "DigitalBrain:Security:StateProtectionKey";

    public static string Storage(string brainName = DefaultBrainName)
    {
        RequireBrainName(brainName);
        return "storage";
    }

    public static string Clustering(string brainName = DefaultBrainName)
    {
        RequireBrainName(brainName);
        return "clustering";
    }

    public static string Reminders(string brainName = DefaultBrainName)
    {
        RequireBrainName(brainName);
        return "reminders";
    }

    public static string JournalResource(string brainName = DefaultBrainName)
    {
        RequireBrainName(brainName);
        return "journal";
    }

    public static string Streams(string brainName = DefaultBrainName)
    {
        RequireBrainName(brainName);
        return "streams";
    }

    public static string PubSub(string brainName = DefaultBrainName)
    {
        RequireBrainName(brainName);
        return "pubsub";
    }

    public const string StreamProviderName = "DigitalBrain";

    public const string PubSubStoreName = "PubSubStore";

    private static void RequireBrainName(string brainName)
        => ArgumentException.ThrowIfNullOrWhiteSpace(brainName);
}
