using System.Buffers;
using System.Text;
using Azure.Storage.Blobs;
using Brain.Kernel.Host.JournalStorage;
using Orleans.Journaling;
using Testcontainers.Azurite;
using Xunit;

namespace Brain.FeasibilityTests.AzureJournal;

public sealed class AzureBlobJournalStorageTests : IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    private BlobServiceClient _blobService = null!;
    private string _containerName = null!;

    public async Task InitializeAsync()
    {
        await _azurite.StartAsync();
        _blobService = new BlobServiceClient(_azurite.GetConnectionString());
        _containerName = "journals-" + Guid.NewGuid().ToString("N");
        await _blobService.CreateBlobContainerAsync(_containerName);
    }

    public async Task DisposeAsync()
    {
        await _azurite.DisposeAsync();
    }

    private AzureBlobJournalStorageProvider CreateProvider()
    {
        return new AzureBlobJournalStorageProvider(new AzureBlobJournalStorageOptions
        {
            ConnectionString = _azurite.GetConnectionString(),
            ContainerName = _containerName,
            JournalFormatKey = "json",
        });
    }

    [Fact]
    public async Task Create_append_read_round_trip()
    {
        var provider = CreateProvider();
        var journalId = JournalId.Create("org", "space", "neuron-a");
        var storage = provider.CreateStorage(journalId);

        var created = await storage.CreateIfNotExistsAsync(
            new Dictionary<string, string> { ["owner"] = "round-trip" },
            CancellationToken.None);
        Assert.True(created);

        var payload = Encoding.UTF8.GetBytes("journal-bytes-1");
        await storage.AppendAsync(new ReadOnlySequence<byte>(payload), CancellationToken.None);

        var captured = new CapturingConsumer();
        await storage.ReadAsync(captured, CancellationToken.None);

        Assert.Equal(payload, captured.Bytes.ToArray());
        Assert.Equal("json", captured.Metadata.Format);
        Assert.Equal("round-trip", captured.Metadata.Properties["owner"]);
        Assert.False(string.IsNullOrWhiteSpace(captured.Metadata.ETag));

        var metadata = await storage.GetMetadataAsync(CancellationToken.None);
        Assert.NotNull(metadata);
        Assert.Equal(captured.Metadata.ETag, metadata!.ETag);
        Assert.Equal("json", metadata.Format);
        Assert.Equal("round-trip", metadata.Properties["owner"]);
    }

    [Fact]
    public async Task Restart_replays_acknowledged_bytes()
    {
        var journalId = JournalId.Create("org", "space", "neuron-restart");
        var firstProvider = CreateProvider();
        var first = firstProvider.CreateStorage(journalId);
        Assert.True(await first.CreateIfNotExistsAsync(cancellationToken: CancellationToken.None));

        var firstChunk = Encoding.UTF8.GetBytes("acked-1");
        var secondChunk = Encoding.UTF8.GetBytes("acked-2");
        await first.AppendAsync(new ReadOnlySequence<byte>(firstChunk), CancellationToken.None);
        await first.AppendAsync(new ReadOnlySequence<byte>(secondChunk), CancellationToken.None);

        var restartedProvider = CreateProvider();
        var restarted = restartedProvider.CreateStorage(journalId);
        var captured = new CapturingConsumer();
        await restarted.ReadAsync(captured, CancellationToken.None);

        var expected = firstChunk.Concat(secondChunk).ToArray();
        Assert.Equal(expected, captured.Bytes.ToArray());
        Assert.Equal("json", captured.Metadata.Format);
        Assert.False(string.IsNullOrWhiteSpace(captured.Metadata.ETag));
    }

    [Fact]
    public async Task Replace_compacts_without_changing_logical_content()
    {
        var provider = CreateProvider();
        var journalId = JournalId.Create("org", "space", "neuron-replace");
        var storage = provider.CreateStorage(journalId);
        Assert.True(await storage.CreateIfNotExistsAsync(
            new Dictionary<string, string> { ["lane"] = "compact" },
            CancellationToken.None));

        var chunks = Enumerable.Range(0, 12)
            .Select(i => Encoding.UTF8.GetBytes($"seg-{i};"))
            .ToArray();
        foreach (var chunk in chunks)
        {
            await storage.AppendAsync(new ReadOnlySequence<byte>(chunk), CancellationToken.None);
        }

        Assert.True(storage.IsCompactionRequested);

        var logical = chunks.SelectMany(c => c).ToArray();
        await storage.ReplaceAsync(new ReadOnlySequence<byte>(logical), CancellationToken.None);
        Assert.False(storage.IsCompactionRequested);

        var captured = new CapturingConsumer();
        await storage.ReadAsync(captured, CancellationToken.None);
        Assert.Equal(logical, captured.Bytes.ToArray());
        Assert.Equal("compact", captured.Metadata.Properties["lane"]);
        Assert.Equal("json", captured.Metadata.Format);

        var metadata = await storage.GetMetadataAsync(CancellationToken.None);
        Assert.NotNull(metadata);
        Assert.Equal("compact", metadata!.Properties["lane"]);
        Assert.Equal(captured.Metadata.ETag, metadata.ETag);
    }

    [Fact]
    public async Task Delete_removes_journal_and_metadata()
    {
        var provider = CreateProvider();
        var journalId = JournalId.Create("org", "space", "neuron-delete");
        var storage = provider.CreateStorage(journalId);
        Assert.True(await storage.CreateIfNotExistsAsync(
            new Dictionary<string, string> { ["k"] = "v" },
            CancellationToken.None));
        await storage.AppendAsync(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("to-delete")), CancellationToken.None);

        await storage.DeleteAsync(CancellationToken.None);

        Assert.Null(await storage.GetMetadataAsync(CancellationToken.None));

        var captured = new CapturingConsumer();
        await storage.ReadAsync(captured, CancellationToken.None);
        Assert.Empty(captured.Bytes);

        var blobName = AzureBlobJournalStorageProvider.ToBlobName(journalId);
        var blob = _blobService.GetBlobContainerClient(_containerName).GetBlobClient(blobName);
        Assert.False(await blob.ExistsAsync());
    }

    [Fact]
    public async Task Stale_writer_is_rejected()
    {
        var journalId = JournalId.Create("org", "space", "neuron-stale");
        var owner = CreateProvider().CreateStorage(journalId);
        var stale = CreateProvider().CreateStorage(journalId);

        Assert.True(await owner.CreateIfNotExistsAsync(cancellationToken: CancellationToken.None));
        await owner.AppendAsync(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("owner-1")), CancellationToken.None);

        Assert.False(await stale.CreateIfNotExistsAsync(cancellationToken: CancellationToken.None));
        await stale.AppendAsync(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("stale-takeover")), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await owner.AppendAsync(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("owner-stale-write")), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await owner.ReplaceAsync(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("owner-stale-replace")), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await owner.DeleteAsync(CancellationToken.None));

        var captured = new CapturingConsumer();
        await CreateProvider().CreateStorage(journalId).ReadAsync(captured, CancellationToken.None);
        Assert.Equal(Encoding.UTF8.GetBytes("owner-1stale-takeover"), captured.Bytes.ToArray());
    }

    [Fact]
    public async Task Metadata_update_honors_etag()
    {
        var provider = CreateProvider();
        var journalId = JournalId.Create("org", "space", "neuron-meta");
        var storage = provider.CreateStorage(journalId);
        Assert.True(await storage.CreateIfNotExistsAsync(
            new Dictionary<string, string> { ["a"] = "1" },
            CancellationToken.None));

        var original = await storage.GetMetadataAsync(CancellationToken.None);
        Assert.NotNull(original);

        var rejected = await storage.UpdateMetadataAsync(
            set: new Dictionary<string, string> { ["a"] = "nope" },
            expectedETag: "\"not-the-etag\"",
            cancellationToken: CancellationToken.None);
        Assert.Null(rejected);

        var stillOriginal = await storage.GetMetadataAsync(CancellationToken.None);
        Assert.NotNull(stillOriginal);
        Assert.Equal("1", stillOriginal!.Properties["a"]);
        Assert.Equal(original!.ETag, stillOriginal.ETag);

        var updated = await storage.UpdateMetadataAsync(
            set: new Dictionary<string, string> { ["a"] = "2", ["b"] = "x" },
            remove: new[] { "missing" },
            expectedETag: original.ETag,
            cancellationToken: CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("2", updated!.Properties["a"]);
        Assert.Equal("x", updated.Properties["b"]);
        Assert.NotEqual(original.ETag, updated.ETag);
    }

    [Fact]
    public async Task Cancellation_does_not_report_success()
    {
        var provider = CreateProvider();
        var journalId = JournalId.Create("org", "space", "neuron-cancel");
        var storage = provider.CreateStorage(journalId);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await storage.CreateIfNotExistsAsync(cancellationToken: cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await storage.AppendAsync(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("x")), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await storage.ReplaceAsync(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("y")), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await storage.DeleteAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await storage.GetMetadataAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await storage.UpdateMetadataAsync(set: new Dictionary<string, string> { ["k"] = "v" }, cancellationToken: cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await storage.ReadAsync(new CapturingConsumer(), cts.Token));

        Assert.Null(await storage.GetMetadataAsync(CancellationToken.None));
        Assert.False(await _blobService.GetBlobContainerClient(_containerName)
            .GetBlobClient(AzureBlobJournalStorageProvider.ToBlobName(journalId))
            .ExistsAsync());
    }

    private sealed class CapturingConsumer : IJournalStorageConsumer
    {
        public List<byte> Bytes { get; } = new();
        public IJournalMetadata Metadata { get; private set; } = JournalMetadata.Empty;

        public void Read(JournalBufferReader buffer, IJournalMetadata? metadata)
        {
            Metadata = metadata ?? JournalMetadata.Empty;
            if (buffer.Length <= 0)
            {
                return;
            }

            var destination = new byte[buffer.Length];
            buffer.Read(destination);
            Bytes.AddRange(destination);
        }
    }
}
