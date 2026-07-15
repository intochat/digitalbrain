extern alias McpProject;

using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Kernel.Features;
using FeatureReleaseWriter = DigitalBrain.FeatureBuilder.FeatureReleaseWriter;
using BuilderFeatureSourceFile = DigitalBrain.FeatureBuilder.FeatureSourceFile;
using BuilderFeatureSourceSnapshot = DigitalBrain.FeatureBuilder.FeatureSourceSnapshot;
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

    [Fact]
    public async Task Runtime_authored_source_is_published_immutably_outside_release_metadata()
    {
        var blobs = new BarrierBlobServiceClient();
        var publisher = new FeatureArtifactPublisher(blobs);
        var source = new DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot(
            "src/Feature/Feature.csproj",
            "tests/Feature.Scenarios/Feature.Scenarios.csproj",
            [
                new DigitalBrain.Kernel.Contracts.FeatureSourceFile("src/Feature/Feature.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                new DigitalBrain.Kernel.Contracts.FeatureSourceFile("src/Feature/Feature.cs", "namespace Example; public sealed class Feature;"),
                new DigitalBrain.Kernel.Contracts.FeatureSourceFile("tests/Feature.Scenarios/Feature.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
            ]);
        var builderSource = new BuilderFeatureSourceSnapshot(
            source.ImplementationProjectPath,
            source.ScenarioProjectPath,
            source.Files.Select(file => new BuilderFeatureSourceFile(file.Path, file.Content)).ToArray());
        var sourceReference = FeatureReleaseWriter.ComputeSourceReference(builderSource);
        var digest = new ReleaseDigest(new string('9', 64));
        var metadata = new FeatureReleaseMetadata(
            digest,
            sourceReference,
            FeatureSourceKind.RuntimeAuthored,
            [],
            [],
            source);
        var releaseDirectory = Path.Combine(Path.GetTempPath(), "digitalbrain-source-publication", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(releaseDirectory);
        await File.WriteAllTextAsync(Path.Combine(releaseDirectory, "digest.txt"), digest.Value);
        await File.WriteAllTextAsync(Path.Combine(releaseDirectory, "Feature.dll"), "release");
        try
        {
            await publisher.PublishReleaseAsync(metadata, releaseDirectory);

            var published = await publisher.DemandReleaseAsync(digest);
            var restored = await publisher.DemandSourceAsync(sourceReference);

            Assert.Null(published.Source);
            Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(restored));
            Assert.Equal(1, blobs.Container.WriteCount($"sources/{sourceReference[7..]}.json"));
            await publisher.PublishReleaseAsync(metadata, releaseDirectory);
            Assert.Equal(1, blobs.Container.WriteCount($"sources/{sourceReference[7..]}.json"));
        }
        finally
        {
            Directory.Delete(releaseDirectory, true);
        }
    }

    [Fact]
    public async Task Maximum_valid_source_with_json_escaped_content_round_trips()
    {
        const int maximumSourceUtf8Bytes = 4_194_304;
        const int maximumFileUtf8Bytes = 1_048_576;
        const string implementationProject = "src/Feature/Feature.csproj";
        const string scenarioProject = "tests/Feature.Scenarios/Feature.Scenarios.csproj";
        const string projectContent = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>";
        var remaining = maximumSourceUtf8Bytes - (2 * System.Text.Encoding.UTF8.GetByteCount(projectContent));
        var files = new List<DigitalBrain.Kernel.Contracts.FeatureSourceFile>
        {
            new(implementationProject, projectContent),
            new(scenarioProject, projectContent)
        };
        for (var index = 0; remaining > 0; index++)
        {
            var length = Math.Min(remaining, maximumFileUtf8Bytes);
            files.Add(new DigitalBrain.Kernel.Contracts.FeatureSourceFile(
                $"src/Feature/Escaped{index}.cs",
                new string('\u0001', length)));
            remaining -= length;
        }
        var source = new DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot(
            implementationProject,
            scenarioProject,
            files.ToArray());
        var builderSource = new BuilderFeatureSourceSnapshot(
            source.ImplementationProjectPath,
            source.ScenarioProjectPath,
            source.Files.Select(file => new BuilderFeatureSourceFile(file.Path, file.Content)).ToArray());
        var sourceReference = FeatureReleaseWriter.ComputeSourceReference(builderSource);
        var digest = new ReleaseDigest(new string('8', 64));
        var metadata = new FeatureReleaseMetadata(
            digest,
            sourceReference,
            FeatureSourceKind.RuntimeAuthored,
            [],
            [],
            source);
        var releaseDirectory = Path.Combine(Path.GetTempPath(), "digitalbrain-source-publication", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(releaseDirectory);
        await File.WriteAllTextAsync(Path.Combine(releaseDirectory, "digest.txt"), digest.Value);
        await File.WriteAllTextAsync(Path.Combine(releaseDirectory, "Feature.dll"), "release");
        try
        {
            var blobs = new BarrierBlobServiceClient();
            var publisher = new FeatureArtifactPublisher(blobs);

            await publisher.PublishReleaseAsync(metadata, releaseDirectory);
            var publishedBytes = blobs.Container.ReadEntry($"sources/{sourceReference[7..]}.json").Content.Length;
            var restored = await publisher.DemandSourceAsync(sourceReference);

            Assert.True(publishedBytes > 8_388_608, $"Expected JSON escaping to exceed 8 MiB, but it used {publishedBytes} bytes.");
            Assert.Equal(source.ImplementationProjectPath, restored.ImplementationProjectPath);
            Assert.Equal(source.ScenarioProjectPath, restored.ScenarioProjectPath);
            Assert.Equal(source.Files, restored.Files);
        }
        finally
        {
            Directory.Delete(releaseDirectory, true);
        }
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
        private readonly Dictionary<string, BlobEntry> entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> writes = new(StringComparer.Ordinal);
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

        public override BlobClient GetBlobClient(string blobName) => new BarrierBlobClient(this, blobName);

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
            lock (gate) return entries.Values.Single().Content.ToArray();
        }

        public BlobEntry ReadEntry(string blobName)
        {
            lock (gate)
            {
                if (!entries.TryGetValue(blobName, out var current))
                    throw new RequestFailedException(404, "The blob does not exist.");
                return current with { Content = current.Content.ToArray() };
            }
        }

        public bool Exists(string blobName)
        {
            lock (gate) return entries.ContainsKey(blobName);
        }

        public int WriteCount(string blobName)
        {
            lock (gate) return writes.GetValueOrDefault(blobName);
        }

        public async Task WriteAsync(
            string blobName,
            byte[] content,
            BlobRequestConditions? conditions,
            CancellationToken cancellationToken)
        {
            if (conflictEveryWrite)
                throw new RequestFailedException(412, "The blob changed concurrently.");
            long? fence = null;
            try
            {
                var root = JsonDocument.Parse(content).RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("publicationFence", out var property)) fence = property.GetInt64();
            }
            catch (JsonException)
            {
            }
            Task? wait = null;
            lock (gate)
            {
                if (!blocked && fence is not null && blockedFence == fence)
                {
                    blocked = true;
                    blockedSignal.TrySetResult();
                    wait = releaseSignal.Task;
                }
            }
            if (wait is not null) await wait.WaitAsync(cancellationToken);
            lock (gate)
            {
                entries.TryGetValue(blobName, out var entry);
                if (conditions?.IfNoneMatch == ETag.All && entry is not null)
                    throw new RequestFailedException(409, "The blob was created concurrently.");
                if (conditions?.IfMatch is { } expected && (entry is null || entry.ETag != expected))
                    throw new RequestFailedException(412, "The blob changed concurrently.");
                entries[blobName] = new BlobEntry(content.ToArray(), new ETag($"\"{Guid.NewGuid():N}\""));
                writes[blobName] = writes.GetValueOrDefault(blobName) + 1;
            }
        }
    }

    private sealed class BarrierBlobClient(BarrierBlobContainerClient container, string blobName) : BlobClient
    {
        public override Task<Response<bool>> ExistsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Response.FromValue(container.Exists(blobName), null!));
        }

        public override Task<Response<BlobProperties>> GetPropertiesAsync(
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = container.ReadEntry(blobName);
            var properties = BlobsModelFactory.BlobProperties(contentLength: entry.Content.LongLength, eTag: entry.ETag);
            return Task.FromResult(Response.FromValue(properties, null!));
        }

        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions options = null!,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = container.ReadEntry(blobName);
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
            await container.WriteAsync(blobName, content.ToArray(), options.Conditions, cancellationToken);
            return null!;
        }

        public override async Task<Response<BlobContentInfo>> UploadAsync(
            Stream content,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            using var output = new MemoryStream();
            await content.CopyToAsync(output, cancellationToken);
            await container.WriteAsync(
                blobName,
                output.ToArray(),
                overwrite ? null : new BlobRequestConditions { IfNoneMatch = ETag.All },
                cancellationToken);
            return null!;
        }
    }

    private sealed record BlobEntry(byte[] Content, ETag ETag);
}
