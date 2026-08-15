namespace DigitalBrain.Aspire;

// Must stay aligned with DigitalBrain.Aspire.Hosting.DigitalBrainNames — the silo/client package cannot reference Hosting.
public static class DigitalBrainResourceNames
{
    public const string DefaultBrainName = "brain";

    public const string JournalConnectionName = "journal";

    public const string ModulesConfigurationKey = "DigitalBrain:Modules";
    public const string OwnerConfigurationKey = "DigitalBrain:Owner";
    public const string DefaultOwner = "dev";
    public const string StateProtectionKeyConfigurationKey = "DigitalBrain:Security:StateProtectionKey";

    public const string Storage = "storage";
    public const string Clustering = "clustering";
    public const string Reminders = "reminders";
    public const string JournalResource = "journal";
    public const string Streams = "streams";
    public const string PubSub = "pubsub";

    public const string StreamProviderName = "DigitalBrain";
    public const string PubSubStoreName = "PubSubStore";
}
