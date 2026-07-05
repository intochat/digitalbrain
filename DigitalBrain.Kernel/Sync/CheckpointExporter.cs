using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Sync;

// Plain, unit-testable exporter: no Orleans/Blob dependencies of its own — checkpointFor/upload are delegates
// so callers (CheckpointBackupTrigger for the real path, tests for the fake path) supply the actual grain/blob
// plumbing. One blob per neuron id, named "{userScope}/{neuronId}.checkpoint". Each neuron's checkpoint+upload
// is independent (different grain, different blob), so they run concurrently via Task.WhenAll rather than one
// at a time; the returned manifest's Entries still preserve neuronIds order regardless of completion order,
// since Task.WhenAll's result array lines up with the order of the source sequence, not completion order.
public sealed class CheckpointExporter(
    IReadOnlyList<string> neuronIds,
    Func<string, Task<ProtectedCheckpoint>> checkpointFor,
    Func<string, byte[], Task> upload)
{
    public async Task<SyncManifest> ExportAsync(string userScope)
    {
        var entries = await Task.WhenAll(neuronIds.Select(async neuronId =>
        {
            var protectedCheckpoint = await checkpointFor(neuronId);
            var blobName = $"{userScope}/{neuronId}.checkpoint";
            await upload(blobName, protectedCheckpoint.EncryptedSnapshot);
            return new SyncManifestEntry(neuronId, blobName, protectedCheckpoint.TakenAt);
        }));

        return new SyncManifest(userScope, entries, DateTimeOffset.UtcNow);
    }
}
