namespace DigitalBrain.Aspire;

// Single source of truth for resource/connection names and configuration keys, physically
// linked into both DigitalBrain.Aspire (silo/client) and DigitalBrain.Aspire.Hosting (AppHost)
// because neither project can reference the other. Each assembly compiles its own copy of
// this public type — an assembly referencing both packages must alias one of them.
public static class DigitalBrainNames
{
    public const string DefaultBrain = "brain";
    public const string DefaultOwner = "dev";

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
    public const string Modules = "DigitalBrain:Modules";
    public const string StateProtectionKey = "DigitalBrain:Security:StateProtectionKey";
}
