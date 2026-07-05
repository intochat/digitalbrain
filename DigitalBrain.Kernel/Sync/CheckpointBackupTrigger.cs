using Azure.Storage.Blobs;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Kernel;

namespace DigitalBrain.Kernel.Sync;

// Thin wiring of CheckpointExporter to the real Orleans/Blob dependencies. Exposes a single method for the
// MCP tool surface or a future scheduled trigger to call — no background timer here, that's a later task's call.
public sealed class CheckpointBackupTrigger(IGrainFactory grains, CheckpointProtector protector, BlobContainerClient syncContainer)
{
    // V1 fixed neuron-id scope: the nine singleton neurons the kernel warms up at startup (Program.cs), not a
    // general per-user neuron enumeration (no such registry exists yet).
    private static readonly string[] V1NeuronIds =
    [
        "status-main", "ino-main", "ino-editor-main", "context-main",
        "db-main", "chart-main", "session-main", "automation-main", "market-data-main"
    ];

    public Task<SyncManifest> BackupAsync(string userScope)
    {
        var exporter = new CheckpointExporter(
            V1NeuronIds,
            checkpointFor: async neuronId =>
            {
                // grains.GetGrain<INeuron>(neuronId) does NOT work here: INeuron is implemented by 40+ distinct
                // concrete grain classes in this kernel (see IGoogleAuthNeuron.cs's and TelegramChatNeuron.cs's
                // own comments on this exact ambiguity, and Neuron.BranchAsync's GrainId.Create workaround for
                // the same reason) — Orleans can't pick a class from the interface alone when more than one
                // class implements it. NeuronResolver.Resolve already carries the neuronId-to-concrete-interface
                // map used by the gRPC gateway (GatewayService/UiGatewayService), so reuse it here rather than
                // duplicating a second, drifting copy of that switch. Its INeuron return type still exposes
                // CreateCheckpointAsync (declared on INeuron, DigitalBrain.Core/INeuron.cs:18) regardless of
                // which derived interface actually resolved the grain.
                var neuron = NeuronResolver.Resolve(grains, neuronId);
                var checkpoint = await neuron.CreateCheckpointAsync();
                return protector.Protect(checkpoint);
            },
            upload: async (blobName, bytes) =>
            {
                await syncContainer.GetBlobClient(blobName).UploadAsync(new BinaryData(bytes), overwrite: true);
            });

        return exporter.ExportAsync(userScope);
    }
}
