using Azure.Storage.Blobs;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Kernel;

namespace DigitalBrain.Kernel.Sync;

// Thin wiring of CheckpointImporter to the real Orleans/Blob dependencies — the reverse leg of
// CheckpointBackupTrigger (Task 21). Exposes a single method for the MCP tool surface or a future
// scheduled trigger to call, same as CheckpointBackupTrigger.
public sealed class CheckpointRestoreTrigger(IGrainFactory grains, CheckpointProtector protector, BlobContainerClient syncContainer)
{
    public Task RestoreAsync(SyncManifest manifest)
    {
        var importer = new CheckpointImporter(
            download: async blobName =>
            {
                var response = await syncContainer.GetBlobClient(blobName).DownloadContentAsync();
                return response.Value.Content.ToArray();
            },
            restore: async (neuronId, bytes, takenAt) =>
            {
                // CheckpointProtector.Unprotect only decrypts EncryptedSnapshot into the Synapse list — Source
                // and TakenAt are plaintext siblings it echoes back verbatim (CheckpointProtector.cs:16-21), not
                // anything recovered from the ciphertext. So the real backup TakenAt (SyncManifestEntry.TakenAt,
                // set honestly by CheckpointExporter from the original Checkpoint's own TakenAt) must be passed
                // in here; fabricating DateTimeOffset.UtcNow instead would silently stamp the reconstructed
                // Checkpoint with this restore's own clock rather than when the backup was actually taken.
                var protectedCheckpoint = new ProtectedCheckpoint(
                    Source: new NeuronId(neuronId), EncryptedSnapshot: bytes, TakenAt: takenAt);
                var checkpoint = protector.Unprotect(protectedCheckpoint);
                // grains.GetGrain<INeuron>(neuronId) does NOT work here — see CheckpointBackupTrigger's own
                // comment on this exact ambiguity: INeuron is implemented by 40+ distinct concrete grain classes
                // in this kernel, so Orleans can't pick a class from the interface alone. NeuronResolver.Resolve
                // already carries the neuronId-to-concrete-interface map used by the gRPC gateway and by
                // CheckpointBackupTrigger, so reuse it here rather than duplicating a second, drifting copy of
                // that switch. Its INeuron return type still exposes RestoreCheckpointAsync (declared on INeuron,
                // DigitalBrain.Core/INeuron.cs:20) regardless of which derived interface actually resolved the
                // grain.
                var neuron = NeuronResolver.Resolve(grains, neuronId);
                // Re-seeds the incoming journal without re-dispatching handlers — the correct semantics for a
                // bootstrap restore (historical events shouldn't re-fire their side effects on import).
                await neuron.RestoreCheckpointAsync(checkpoint);
            });

        return importer.RestoreAsync(manifest);
    }
}
