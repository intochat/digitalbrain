using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Market;
using DigitalBrain.Kernel.Sync;
using DigitalBrain.TestKit;
using DigitalBrain.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Sync;

// Integration coverage against a REAL Orleans TestCluster (not the delegate-faked CheckpointExporterTests):
// proves CheckpointBackupTrigger's NeuronResolver.Resolve-based dispatch actually activates and checkpoints
// all nine V1 neuron ids. This specifically guards against a raw grains.GetGrain<INeuron>(neuronId) regression
// — this kernel's own codebase documents that call as ambiguous once INeuron has more than one concrete grain
// class (see IGoogleAuthNeuron.cs's and TelegramChatNeuron.cs's comments, and Neuron.BranchAsync's
// GrainId.Create workaround for the same root cause), which is exactly the situation here across the nine
// different neuron types.
public class CheckpointBackupTriggerTests : NeuronTestBase
{
    // MarketDataNeuron (one of the nine V1 ids) needs a real IMarketDataApiClient to activate; a fake stands in
    // here the same way MarketDataNeuronTests.cs does, since this test never actually calls the market API.
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IMarketDataApiClient>(new FakeMarketDataApiClient()));

    [Fact]
    public async Task BackupAsync_UploadsOneBlobPerV1NeuronId_AgainstRealGrains()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b => b.AddAssembly(typeof(Synapse).Assembly));
        using var provider = services.BuildServiceProvider();
        var protector = new CheckpointProtector(provider.GetRequiredService<Serializer>(), new PassThroughNeuronStateProtector());

        var syncContainer = new RecordingBlobContainerClient();
        var trigger = new CheckpointBackupTrigger(Cluster.GrainFactory, protector, syncContainer);

        var manifest = await trigger.BackupAsync("demo-user");

        Assert.Equal(8, manifest.Entries.Count);
        Assert.Equal(8, syncContainer.Uploads.Count);
        Assert.All(manifest.Entries, entry => Assert.StartsWith("demo-user/", entry.BlobName));
        Assert.All(syncContainer.Uploads.Values, bytes => Assert.NotEmpty(bytes));
    }

    // The pipeline test above proves the wiring works end-to-end, but it does NOT discriminate between the
    // fixed and pre-fix-buggy NeuronResolver: IDemoNeuron (the old, wrong fallback target for
    // "automation-main"/"market-data-main") activates with no extra dependency and its CreateCheckpointAsync
    // still returns a non-empty snapshot, so every assertion above would still pass even if those two switch
    // cases regressed back to the `_ => GetGrain<IDemoNeuron>(...)` default. This test targets the actual
    // resolved interface directly instead, which only the correct switch arm produces — an Orleans grain
    // reference's runtime type implements exactly the interface it was requested with (plus that interface's
    // own base interfaces), not sibling interfaces, so a reference obtained via GetGrain<IDemoNeuron> is NOT
    // assignable to IAutomationNeuron/IMarketDataNeuron and vice versa. Verified red/green against a temporary
    // revert of the NeuronResolver.cs fix (see task-21-report.md) to confirm this actually discriminates.
    [Fact]
    public void NeuronResolver_Resolves_AutomationAndMarketData_ToTheirOwnInterfaces_NotIDemoNeuronFallback()
    {
        Assert.IsAssignableFrom<IAutomationNeuron>(NeuronResolver.Resolve(Cluster.GrainFactory, "automation-main"));
        Assert.IsAssignableFrom<IMarketDataNeuron>(NeuronResolver.Resolve(Cluster.GrainFactory, "market-data-main"));
    }

    // Azure SDK clients are designed for subclass-based test doubles (protected parameterless constructor +
    // virtual members, no mocking library needed) — same pattern as PackConfigBackingStoreSelectionTests.cs's
    // NoNetworkBlobServiceClient, extended here to actually record what was uploaded instead of no-op'ing it.
    private sealed class RecordingBlobContainerClient : BlobContainerClient
    {
        // ConcurrentDictionary: CheckpointExporter now uploads all V1 neurons' blobs concurrently
        // (Task.WhenAll), so multiple RecordingBlobClient instances write into this from different tasks.
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
    }
}
