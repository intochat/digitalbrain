namespace DigitalBrain.Kernel.Sync;

public sealed record SyncManifestEntry(string NeuronId, string BlobName, DateTimeOffset TakenAt);

public sealed record SyncManifest(string UserScope, IReadOnlyList<SyncManifestEntry> Entries, DateTimeOffset ExportedAt);
