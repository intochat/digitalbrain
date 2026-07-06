using DigitalBrain.Core;
using DigitalBrain.Kernel.Sync;
using Xunit;

namespace DigitalBrain.Tests.Sync;

public class CheckpointExporterTests
{
    [Fact]
    public async Task ExportAsync_UploadsOneBlobPerNeuronId_AndReturnsManifestWithMatchingCount()
    {
        var fakeUploads = new List<string>();
        var exporter = new CheckpointExporter(
            neuronIds: ["status-main", "context-main"],
            checkpointFor: _ => Task.FromResult(new ProtectedCheckpoint(
                Source: new NeuronId("test"), EncryptedSnapshot: [1, 2, 3], TakenAt: DateTimeOffset.UtcNow)),
            upload: (blobName, bytes) => { fakeUploads.Add(blobName); return Task.CompletedTask; });

        var manifest = await exporter.ExportAsync(userScope: "demo-user");

        Assert.Equal(2, manifest.Entries.Count);
        Assert.Equal(2, fakeUploads.Count);
        Assert.All(fakeUploads, name => Assert.StartsWith("demo-user/", name));
    }
}
