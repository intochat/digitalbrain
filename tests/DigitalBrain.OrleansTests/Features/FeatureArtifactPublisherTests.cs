extern alias McpProject;

using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Kernel.Features;
using FeatureArtifactPublisher = McpProject::DigitalBrain.Mcp.FeatureArtifactPublisher;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureArtifactPublisherTests
{
    private static readonly BrainOwnerId Owner = new("owner-publication-race");
    private static readonly FeatureInstallationId Installation = new("installation-publication-race");

    [Fact]
    public async Task A_blocked_lower_publication_fence_cannot_overwrite_a_higher_fence()
    {
        var blobs = new BarrierBlobServiceClient();
        var publisher = new FeatureArtifactPublisher(blobs);
        await publisher.PublishActiveAsync(Owner, Ticket(1, 'a'));
        blobs.Container.BlockFence(2);

        var stale = publisher.PublishActiveAsync(Owner, Ticket(2, 'b'));
        await blobs.Container.WaitUntilBlockedAsync();
        var winner = await publisher.PublishActiveAsync(Owner, Ticket(3, 'c'));
        blobs.Container.ReleaseBlockedFence();

        var superseded = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => stale);
        var manifest = JsonDocument.Parse(blobs.Container.Read()).RootElement;
        Assert.Equal(3, manifest.GetProperty("publicationFence").GetInt64());
        Assert.Equal(new string('c', 64), manifest.GetProperty("releaseDigest").GetString());
        Assert.Equal(winner.ManifestDigest, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(blobs.Container.Read())));
        Assert.Equal(winner, await publisher.PublishActiveAsync(
            Owner,
            Ticket(3, 'c') with { Subscriptions = ["a-event", "z-event"] }));
        var conflictingContent = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => publisher.PublishActiveAsync(
            Owner,
            Ticket(3, 'c') with { AuthorityDigest = new string('d', 64) }));

        Assert.Equal(FeatureCommandRejectionReason.Conflict, superseded.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, conflictingContent.Reason);
    }

    [Fact]
    public async Task Publication_retry_exhaustion_is_a_typed_unavailable_rejection()
    {
        var blobs = new BarrierBlobServiceClient();
        blobs.Container.ConflictEveryWrite();
        var publisher = new FeatureArtifactPublisher(blobs);

        var unavailable = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            publisher.PublishActiveAsync(Owner, Ticket(1, 'a')));

        Assert.Equal(FeatureCommandRejectionReason.Unavailable, unavailable.Reason);
    }

    private static FeaturePublicationTicket Ticket(long fence, char digestCharacter) => new(
        Installation,
        new ActorId("actor-publication-race"),
        new ReleaseDigest(new string(digestCharacter, 64)),
        new GrantRevision(fence),
        [new FeatureGrantSpec("capability.read", 1, null, "{\"allowedToolIds\":[\"capability.read\"]}")],
        ["z-event", "a-event"],
        fence,
        new string(digestCharacter, 64),
        new string((char)(digestCharacter + 1), 64));

    private sealed class BarrierBlobServiceClient : BlobServiceClient
    {
        public BarrierBlobContainerClient Container { get; } = new();

        public override BlobContainerClient GetBlobContainerClient(string blobContainerName) => Container;
    }

    private sealed class BarrierBlobContainerClient : BlobContainerClient
    {
        private readonly object gate = new();
        private BlobEntry? entry;
        private long? blockedFence;
        private bool blocked;
        private TaskCompletionSource blockedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource releaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool conflictEveryWrite;

        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Response<BlobContainerInfo>>(null!);
        }

        public override BlobClient GetBlobClient(string blobName) => new BarrierBlobClient(this);

        public void BlockFence(long fence)
        {
            lock (gate)
            {
                blockedFence = fence;
                blocked = false;
                blockedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                releaseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public Task WaitUntilBlockedAsync() => blockedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void ReleaseBlockedFence() => releaseSignal.TrySetResult();

        public void ConflictEveryWrite() => conflictEveryWrite = true;

        public byte[] Read()
        {
            lock (gate) return (entry ?? throw new InvalidOperationException()).Content.ToArray();
        }

        public BlobEntry ReadEntry()
        {
            lock (gate)
            {
                var current = entry ?? throw new RequestFailedException(404, "The blob does not exist.");
                return current with { Content = current.Content.ToArray() };
            }
        }

        public async Task WriteAsync(byte[] content, BlobRequestConditions? conditions, CancellationToken cancellationToken)
        {
            if (conflictEveryWrite)
                throw new RequestFailedException(412, "The blob changed concurrently.");
            var fence = JsonDocument.Parse(content).RootElement.GetProperty("publicationFence").GetInt64();
            Task? wait = null;
            lock (gate)
            {
                if (!blocked && blockedFence == fence)
                {
                    blocked = true;
                    blockedSignal.TrySetResult();
                    wait = releaseSignal.Task;
                }
            }
            if (wait is not null) await wait.WaitAsync(cancellationToken);
            lock (gate)
            {
                if (conditions?.IfNoneMatch == ETag.All && entry is not null)
                    throw new RequestFailedException(412, "The blob was created concurrently.");
                if (conditions?.IfMatch is { } expected && (entry is null || entry.ETag != expected))
                    throw new RequestFailedException(412, "The blob changed concurrently.");
                entry = new BlobEntry(content.ToArray(), new ETag($"\"{Guid.NewGuid():N}\""));
            }
        }
    }

    private sealed class BarrierBlobClient(BarrierBlobContainerClient container) : BlobClient
    {
        public override Task<Response<BlobProperties>> GetPropertiesAsync(
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = container.ReadEntry();
            var properties = BlobsModelFactory.BlobProperties(contentLength: entry.Content.LongLength, eTag: entry.ETag);
            return Task.FromResult(Response.FromValue(properties, null!));
        }

        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions options = null!,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = container.ReadEntry();
            if (options?.Conditions?.IfMatch is { } expected && expected != entry.ETag)
                throw new RequestFailedException(412, "The blob changed concurrently.");
            var result = BlobsModelFactory.BlobDownloadStreamingResult(
                new MemoryStream(entry.Content, writable: false),
                BlobsModelFactory.BlobDownloadDetails(contentLength: entry.Content.LongLength, eTag: entry.ETag));
            return Task.FromResult(Response.FromValue(result, null!));
        }

        public override async Task<Response<BlobContentInfo>> UploadAsync(
            BinaryData content,
            BlobUploadOptions options,
            CancellationToken cancellationToken = default)
        {
            await container.WriteAsync(content.ToArray(), options.Conditions, cancellationToken);
            return null!;
        }
    }

    private sealed record BlobEntry(byte[] Content, ETag ETag);
}
