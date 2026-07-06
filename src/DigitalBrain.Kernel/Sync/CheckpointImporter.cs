namespace DigitalBrain.Kernel.Sync;

// Plain, unit-testable importer mirroring CheckpointExporter's shape: no Orleans/Blob dependencies of its own —
// download/restore are delegates so callers (CheckpointRestoreTrigger for the real path, tests for the fake
// path) supply the actual blob/grain plumbing. Sequential (not Task.WhenAll like the exporter): restoring one
// neuron at a time, in manifest order, keeps this predictable for callers that care about ordering, and unlike
// the exporter's independent per-neuron uploads, there's no throughput reason here to parallelize side effects
// that mutate live grain state.
public sealed class CheckpointImporter(
    Func<string, Task<byte[]>> download,
    Func<string, byte[], DateTimeOffset, Task> restore)
{
    public async Task RestoreAsync(SyncManifest manifest)
    {
        foreach (var entry in manifest.Entries)
        {
            var bytes = await download(entry.BlobName);
            await restore(entry.NeuronId, bytes, entry.TakenAt);
        }
    }
}
