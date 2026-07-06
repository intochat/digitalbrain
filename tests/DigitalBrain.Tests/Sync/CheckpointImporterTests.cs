using DigitalBrain.Kernel.Sync;
using Xunit;

namespace DigitalBrain.Tests.Sync;

public class CheckpointImporterTests
{
    [Fact]
    public async Task RestoreAsync_CallsRestoreOncePerManifestEntry_InManifestOrder_WithDownloadedBytesAndTakenAt()
    {
        var downloads = new Dictionary<string, byte[]>
        {
            ["demo-user/status-main.checkpoint"] = [1, 2, 3],
            ["demo-user/context-main.checkpoint"] = [4, 5, 6],
        };
        var statusTakenAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var contextTakenAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var restoreCalls = new List<(string NeuronId, byte[] Bytes, DateTimeOffset TakenAt)>();
        var importer = new CheckpointImporter(
            download: blobName => Task.FromResult(downloads[blobName]),
            restore: (neuronId, bytes, takenAt) => { restoreCalls.Add((neuronId, bytes, takenAt)); return Task.CompletedTask; });

        var manifest = new SyncManifest(
            UserScope: "demo-user",
            Entries:
            [
                new SyncManifestEntry("status-main", "demo-user/status-main.checkpoint", statusTakenAt),
                new SyncManifestEntry("context-main", "demo-user/context-main.checkpoint", contextTakenAt),
            ],
            ExportedAt: DateTimeOffset.UtcNow);

        await importer.RestoreAsync(manifest);

        Assert.Equal(2, restoreCalls.Count);
        Assert.Equal("status-main", restoreCalls[0].NeuronId);
        Assert.Equal([1, 2, 3], restoreCalls[0].Bytes);
        Assert.Equal(statusTakenAt, restoreCalls[0].TakenAt);
        Assert.Equal("context-main", restoreCalls[1].NeuronId);
        Assert.Equal([4, 5, 6], restoreCalls[1].Bytes);
        Assert.Equal(contextTakenAt, restoreCalls[1].TakenAt);
    }
}
