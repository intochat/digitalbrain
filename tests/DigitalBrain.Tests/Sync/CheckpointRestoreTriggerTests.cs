using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Sync;
using DigitalBrain.TestKit;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Sync;

// Integration coverage against a REAL Orleans TestCluster (same rationale as CheckpointBackupTriggerTests):
// proves CheckpointRestoreTrigger's NeuronResolver.Resolve-based dispatch (the same fix Task 21 established for
// the backup half) actually downloads, decrypts, and replays a checkpoint into real grains of every V1 neuron
// type, and — the sharper claim — that a genuine cloud->local bootstrap recovers exactly the pre-mutation
// snapshot, not anything the source neuron did afterwards.
public class CheckpointRestoreTriggerTests : NeuronTestBase
{
    [Fact]
    public async Task RestoreAsync_ReplaysEveryV1NeuronsCheckpointSnapshot_IntoItsIncomingJournal_AgainstRealGrains()
    {
        var protector = NewProtector();
        var syncContainer = new RecordingBlobContainerClient();

        var backupTrigger = new CheckpointBackupTrigger(Cluster.GrainFactory, protector, syncContainer);
        var manifest = await backupTrigger.BackupAsync("demo-user");

        // Decrypt what was actually uploaded for each of the nine V1 ids, independently of the trigger under
        // test, so the assertions below don't just re-check the trigger's own bookkeeping.
        var exportedCheckpoints = manifest.Entries.ToDictionary(
            e => e.NeuronId,
            e => protector.Unprotect(new ProtectedCheckpoint(new NeuronId(e.NeuronId), syncContainer.Uploads[e.BlobName], e.TakenAt)));

        var restoreTrigger = new CheckpointRestoreTrigger(Cluster.GrainFactory, protector, syncContainer);
        await restoreTrigger.RestoreAsync(manifest);

        foreach (var entry in manifest.Entries)
        {
            var incoming = await NeuronResolver.Resolve(Cluster.GrainFactory, entry.NeuronId).GetIncomingTimelineAsync();
            var incomingIds = incoming.Select(s => s.SynapseId).ToHashSet();
            var expectedIds = exportedCheckpoints[entry.NeuronId].Snapshot.Select(s => s.SynapseId);

            Assert.All(expectedIds, id => Assert.Contains(id, incomingIds));
        }
    }

    // RestoreCheckpointAsync (Neuron.cs) re-seeds a neuron's INCOMING journal from the checkpoint snapshot; it
    // is additive, not a reset (its own comment: "seed ... WITHOUT re-dispatching handlers"). Restoring a
    // checkpoint back onto the SAME still-live grain that produced it can never demonstrate real recovery: by
    // construction, checkpoint.Snapshot is always a subset of what that grain's own journals already contain at
    // export time, and journals only grow afterwards — so nothing "recovered" by such a round trip was ever
    // actually at risk of being lost. A genuine cloud->local bootstrap instead targets a grain that has NEVER
    // seen this content, which is what this test sets up: two distinct ids ("chart-recover-source" and
    // "chart-recover-target") that both resolve via NeuronResolver's "chart-" wildcard case to the same
    // concrete ChartNeuron class, but are two independent grain activations with independent journals — the
    // same relationship a "cloud" instance's grain and a fresh "local" instance's grain of the same neuron id
    // would have in production. The manifest entry is hand-retargeted from source to target (SyncManifest is a
    // plain record; CheckpointExporter always names/targets after the id it actually backed up, so simulating
    // "restore into a different, fresh environment's same-named grain" requires constructing that manifest
    // entry directly rather than exporting it).
    [Fact]
    public async Task RestoreAsync_BootstrapsAFreshGrain_WithOnlyThePreMutationSnapshot_NotLaterSourceMutations()
    {
        var protector = NewProtector();
        var syncContainer = new RecordingBlobContainerClient();

        var source = NeuronResolver.Resolve(Cluster.GrainFactory, "chart-recover-source");

        // ChartInteraction is a harmless marker here: ChartNeuron.HandleAsync(ChartInteraction) no-ops when
        // there's no prior session for the surface, so delivering it has no side effect beyond landing the
        // synapse itself in the incoming journal (exactly what's needed as a distinctive, inert marker).
        var preBackupMarker = new ChartInteraction("s1", "pre-backup", new Dictionary<string, object?>());
        await source.DeliverAsync(preBackupMarker);

        var exporter = new CheckpointExporter(
            neuronIds: ["chart-recover-source"],
            checkpointFor: async neuronId =>
            {
                var checkpoint = await NeuronResolver.Resolve(Cluster.GrainFactory, neuronId).CreateCheckpointAsync();
                return protector.Protect(checkpoint);
            },
            upload: async (blobName, bytes) => await syncContainer.GetBlobClient(blobName).UploadAsync(new BinaryData(bytes), overwrite: true));
        var manifest = await exporter.ExportAsync("demo-user");

        // The source keeps evolving after the backup — this must never reach the fresh bootstrap target below.
        var postBackupMutation = new ChartInteraction("s1", "post-mutation", new Dictionary<string, object?>());
        await source.DeliverAsync(postBackupMutation);

        var bootstrapManifest = manifest with
        {
            Entries = [manifest.Entries[0] with { NeuronId = "chart-recover-target" }]
        };

        var restoreTrigger = new CheckpointRestoreTrigger(Cluster.GrainFactory, protector, syncContainer);
        await restoreTrigger.RestoreAsync(bootstrapManifest);

        var target = NeuronResolver.Resolve(Cluster.GrainFactory, "chart-recover-target");
        var targetIncoming = await target.GetIncomingTimelineAsync();

        Assert.Contains(targetIncoming, s => s.SynapseId == preBackupMarker.SynapseId);
        Assert.DoesNotContain(targetIncoming, s => s.SynapseId == postBackupMutation.SynapseId);
    }

    private static CheckpointProtector NewProtector()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b => b.AddAssembly(typeof(Synapse).Assembly));
        var provider = services.BuildServiceProvider();
        return new CheckpointProtector(provider.GetRequiredService<Serializer>(), new PassThroughNeuronStateProtector());
    }

    // Extends Task 21's RecordingBlobContainerClient/RecordingBlobClient (CheckpointBackupTriggerTests.cs) with
    // download support, since restore needs to read back what backup uploaded.
    private sealed class RecordingBlobContainerClient : BlobContainerClient
    {
        public ConcurrentDictionary<string, byte[]> Uploads { get; } = new();

        public override BlobClient GetBlobClient(string blobName) => new RecordingBlobClient(blobName, Uploads);
    }

    private sealed class RecordingBlobClient(string name, ConcurrentDictionary<string, byte[]> uploads) : BlobClient
    {
        public override Task<Response<BlobContentInfo>> UploadAsync(BinaryData content, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            uploads[name] = content.ToArray();
            return Task.FromResult<Response<BlobContentInfo>>(null!);
        }

        public override Task<Response<BlobDownloadResult>> DownloadContentAsync(CancellationToken cancellationToken = default)
        {
            if (!uploads.TryGetValue(name, out var bytes))
                throw new InvalidOperationException($"No blob was uploaded under name '{name}' before this download attempt.");

            var result = BlobsModelFactory.BlobDownloadResult(content: new BinaryData(bytes));
            return Task.FromResult(Response.FromValue(result, null!));
        }
    }
}
