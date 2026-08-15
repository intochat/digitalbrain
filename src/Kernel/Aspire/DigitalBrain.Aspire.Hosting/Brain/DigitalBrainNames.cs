namespace DigitalBrain.Aspire.Hosting;

// Must stay aligned with DigitalBrain.Aspire.DigitalBrainResourceNames — Hosting cannot reference that package.
public static class DigitalBrainNames
{
    public const string DefaultBrain = "brain";
    public const string Storage = "storage";
    public const string Clustering = "clustering";
    public const string Reminders = "reminders";
    public const string Journal = "journal";
    public const string Streams = "streams";
    public const string PubSub = "pubsub";
    public const string JournalConnection = "journal";
    public const string StreamProvider = "DigitalBrain";
    public const string PubSubStore = "PubSubStore";
    public const string Owner = "DigitalBrain:Owner";
    public const string DefaultOwner = "dev";
    public const string StateProtectionKey = "DigitalBrain:Security:StateProtectionKey";
}
