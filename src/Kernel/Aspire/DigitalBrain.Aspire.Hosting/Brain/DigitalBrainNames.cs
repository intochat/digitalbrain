using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

// AppHost-facing aliases over Abstractions resource names (single product vocabulary).
public static class DigitalBrainNames
{
    public const string DefaultBrain = DigitalBrainResourceNames.DefaultBrainName;
    public const string Storage = DigitalBrainResourceNames.Storage;
    public const string Clustering = DigitalBrainResourceNames.Clustering;
    public const string Reminders = DigitalBrainResourceNames.Reminders;
    public const string Journal = DigitalBrainResourceNames.JournalResource;
    public const string Streams = DigitalBrainResourceNames.Streams;
    public const string PubSub = DigitalBrainResourceNames.PubSub;
    public const string JournalConnection = DigitalBrainResourceNames.JournalConnectionName;
    public const string StreamProvider = DigitalBrainResourceNames.StreamProviderName;
    public const string PubSubStore = DigitalBrainResourceNames.PubSubStoreName;
    public const string StateProtectionKey = DigitalBrainResourceNames.StateProtectionKeyConfigurationKey;
}