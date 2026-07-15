using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.FeatureBuilder;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection;
using BuilderFeatureSourceFile = DigitalBrain.FeatureBuilder.FeatureSourceFile;
using BuilderFeatureSourceSnapshot = DigitalBrain.FeatureBuilder.FeatureSourceSnapshot;
namespace DigitalBrain.Mcp;

public sealed record FeatureSourceInput(string Path, string Content);
public sealed record FeatureBuildSubmission(string ImplementationProjectPath, string ScenarioProjectPath, IReadOnlyList<FeatureSourceInput> Files, FeatureSourceKind SourceKind);
public sealed record FeatureBuildArtifact(
    FeatureReleaseMetadata Release,
    FeatureScenarioResult Scenarios,
    FeatureVerificationEvidence? VerificationEvidence = null)
{
    public FeatureVerificationEvidence Evidence =>
        VerificationEvidence ?? FeatureBuildEvidence.Project(Release.SourceReference, Scenarios, []);
}
public sealed record FeatureBuildReview(FeatureVerificationEvidence Evidence, FeatureBuildArtifact? Artifact);
public interface IFeatureBuildEndpoint
{
    Task<FeatureBuildArtifact> BuildAsync(FeatureBuildSubmission submission, CancellationToken cancellationToken = default);
    async Task<FeatureBuildReview> VerifyAsync(FeatureBuildSubmission submission, CancellationToken cancellationToken = default)
    {
        var artifact = await BuildAsync(submission, cancellationToken).ConfigureAwait(false);
        var scenarios = artifact.Scenarios;
        var passed = scenarios.Total > 0 && scenarios.Passed == scenarios.Total && scenarios.Failed == 0 && scenarios.Skipped == 0;
        return new FeatureBuildReview(artifact.Evidence, passed ? artifact : null);
    }
}
public interface IFeatureArtifactCatalog
{
    Task<FeatureReleaseMetadata> DemandReleaseAsync(ReleaseDigest digest, CancellationToken cancellationToken = default);
    Task<DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot> DemandSourceAsync(string sourceReference, CancellationToken cancellationToken = default) =>
        Task.FromException<DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot>(
            new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable));
}
public sealed class FeatureBuildEndpoint(FeatureArtifactPublisher artifacts, TimeProvider timeProvider) : IFeatureBuildEndpoint
{
    private const int MaximumProcessOutputCharacters = 1_048_576;
    public async Task<FeatureBuildArtifact> BuildAsync(FeatureBuildSubmission submission, CancellationToken cancellationToken = default)
    {
        var review = await VerifyAsync(submission, cancellationToken).ConfigureAwait(false);
        return review.Artifact
            ?? throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    public async Task<FeatureBuildReview> VerifyAsync(FeatureBuildSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(submission.Files);
        var source = new BuilderFeatureSourceSnapshot(
            submission.ImplementationProjectPath,
            submission.ScenarioProjectPath,
            submission.Files.Select(file => new BuilderFeatureSourceFile(file.Path, file.Content)).ToArray());
        var root = Path.Combine(Path.GetTempPath(), "digitalbrain-feature-endpoint", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "releases");
        var requestPath = Path.Combine(root, "request.json");
        Directory.CreateDirectory(root);
        try
        {
            var request = new BuilderCommand(
                source.ImplementationProjectPath,
                source.ScenarioProjectPath,
                source.Files.Select(file => new BuilderFile(file.Path, Convert.ToBase64String(Encoding.UTF8.GetBytes(file.Content)))).ToArray(),
                PackageFeed(),
                output,
                timeProvider.GetUtcNow().Add(FeatureBuildPipeline.MaximumRequestDuration));
            await File.WriteAllBytesAsync(requestPath, JsonSerializer.SerializeToUtf8Bytes(request), cancellationToken);
            var verification = await RunBuilderAsync(requestPath, cancellationToken);
            var expectedSourceReference = FeatureReleaseWriter.ComputeSourceReference(source);
            if (!string.Equals(verification.SourceReference, expectedSourceReference, StringComparison.Ordinal))
                throw new InvalidDataException("FeatureBuilder returned evidence for another source snapshot.");
            var evidence = FeatureBuildEvidence.Project(
                verification.SourceReference,
                verification.Scenarios,
                verification.Artifacts);
            if (verification.Release is null)
                return new FeatureBuildReview(evidence, null);
            var release = verification.Release;
            if (!string.Equals(release.SourceReference, verification.SourceReference, StringComparison.Ordinal) ||
                !string.Equals(Path.GetFullPath(release.ReleaseDirectory), Path.GetFullPath(Path.Combine(output, release.Digest)), PathComparison))
                throw new InvalidDataException("FeatureBuilder returned a release outside its verified source coordinate.");
            var sourceSnapshot = new DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot(
                submission.ImplementationProjectPath,
                submission.ScenarioProjectPath,
                submission.Files.Select(file => new DigitalBrain.Kernel.Contracts.FeatureSourceFile(file.Path, file.Content)).ToArray());
            var metadata = new FeatureReleaseMetadata(
                new ReleaseDigest(release.Digest),
                release.SourceReference,
                submission.SourceKind,
                release.Manifest.RequestedCapabilities.ToArray(),
                release.Manifest.AssemblyReferences.ToArray(),
                sourceSnapshot);
            await artifacts.PublishReleaseAsync(metadata, release.ReleaseDirectory, cancellationToken);
            return new FeatureBuildReview(evidence, new FeatureBuildArtifact(metadata, release.Scenarios, evidence));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    private static async Task<FeatureBuildVerification> RunBuilderAsync(string requestPath, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var inherited = start.Environment.ToArray();
        start.Environment.Clear();
        foreach (var variable in inherited)
        {
            if (variable.Key is "PATH" or "Path" or "DOTNET_ROOT" or "DOTNET_HOST_PATH" or
                "SystemRoot" or "SYSTEMROOT" or "TEMP" or "TMP" or "TMPDIR" or "HOME")
                start.Environment[variable.Key] = variable.Value;
        }
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.ArgumentList.Add(typeof(FeatureBuildPipeline).Assembly.Location);
        start.ArgumentList.Add(requestPath);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("FeatureBuilder could not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(FeatureBuildPipeline.MaximumRequestDuration.Add(TimeSpan.FromSeconds(5)));
        var outputTask = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var errorTask = ReadBoundedAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "FeatureBuilder rejected the source." : error.Trim());
        return JsonSerializer.Deserialize<FeatureBuildVerification>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("FeatureBuilder returned no verification evidence.");
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken);
            if (read == 0) return result.ToString();
            if (result.Length + read > MaximumProcessOutputCharacters)
                throw new InvalidDataException("FeatureBuilder output exceeded its bound.");
            result.Append(buffer, 0, read);
        }
    }
    private static string PackageFeed()
    {
        var configured = Environment.GetEnvironmentVariable("DigitalBrain__FeatureBuilder__OfflineFeed");
        var path = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(AppContext.BaseDirectory, "feature-packages") : configured);
        return Directory.Exists(path) ? path : throw new DirectoryNotFoundException("The FeatureBuilder offline package feed is unavailable.");
    }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private sealed record BuilderCommand(
        string ImplementationProjectPath,
        string ScenarioProjectPath,
        IReadOnlyList<BuilderFile> Files,
        string OfflineFeedDirectory,
        string OutputDirectory,
        DateTimeOffset Deadline);
    private sealed record BuilderFile(string Path, string ContentBase64);
}
internal static class FeatureBuildEvidence
{
    public static FeatureVerificationEvidence Project(
        string sourceReference,
        FeatureScenarioResult scenarios,
        IReadOnlyList<DigitalBrain.FeatureBuilder.FeatureVerificationArtifact> artifacts)
    {
        var results = scenarios.Results.Count == 0
            ? Enumerable.Range(0, scenarios.Total).Select(index =>
            {
                var outcome = index < scenarios.Passed
                    ? DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Passed
                    : index < scenarios.Passed + scenarios.Failed
                        ? DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Failed
                        : DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Skipped;
                return new DigitalBrain.Kernel.Contracts.FeatureScenarioEvidence(
                    $"scenario-{index + 1}",
                    $"Scenario {index + 1}",
                    outcome,
                    outcome == DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Failed ? "Scenario failed." : null,
                    0);
            }).ToArray()
            : scenarios.Results.Select(result => new DigitalBrain.Kernel.Contracts.FeatureScenarioEvidence(
                result.ScenarioId,
                result.Name,
                result.Outcome switch
                {
                    DigitalBrain.FeatureBuilder.FeatureScenarioOutcome.Passed => DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Passed,
                    DigitalBrain.FeatureBuilder.FeatureScenarioOutcome.Failed => DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Failed,
                    DigitalBrain.FeatureBuilder.FeatureScenarioOutcome.Skipped => DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Skipped,
                    _ => throw new InvalidDataException("FeatureBuilder returned an unknown scenario outcome.")
                },
                result.SafeFailure,
                result.DurationMilliseconds)).ToArray();
        return new FeatureVerificationEvidence(
            sourceReference,
            scenarios.Total,
            scenarios.Passed,
            scenarios.Failed,
            scenarios.Skipped,
            results,
            artifacts.Select(artifact => new DigitalBrain.Kernel.Contracts.FeatureVerificationArtifact(
                artifact.Name,
                artifact.MediaType,
                artifact.SizeBytes,
                artifact.Digest)).ToArray());
    }
}
public sealed class FeatureArtifactPublisher([FromKeyedServices("features")] BlobServiceClient blobs) : IFeatureArtifactCatalog
{
    private const string ContainerName = "feature-releases";
    private const int MaximumReleaseFiles = 256;
    private const long MaximumReleaseBytes = 67_108_864;
    private const int MaximumMetadataBytes = 65_536;
    private const int MaximumSerializedSourceBytes = 33_554_432;
    private readonly BlobContainerClient container = blobs.GetBlobContainerClient(ContainerName);
    public async Task PublishReleaseAsync(FeatureReleaseMetadata metadata, string releaseDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!Enum.IsDefined(metadata.SourceKind))
            throw new ArgumentException("The Feature source kind is invalid.", nameof(metadata));
        if (metadata.Source is { } source)
        {
            await PublishSourceAsync(metadata.SourceReference, source, cancellationToken).ConfigureAwait(false);
            metadata = metadata with { Source = null };
        }
        var digest = metadata.Digest;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(releaseDirectory));
        if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new DirectoryNotFoundException("A regular Feature release directory is required.");
        var marker = Path.Combine(root, "digest.txt");
        if (!File.Exists(marker) || !string.Equals(await File.ReadAllTextAsync(marker, cancellationToken), digest.Value, StringComparison.Ordinal))
            throw new InvalidDataException("The Feature release digest marker does not match.");
        var files = ReleaseFiles(root);
        if (files.Length is 0 or > MaximumReleaseFiles)
            throw new InvalidDataException("The Feature release file count is invalid.");
        long total = 0;
        foreach (var path in files)
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Feature release files cannot be filesystem links.");
            total = checked(total + new FileInfo(path).Length);
            if (total > MaximumReleaseBytes)
                throw new InvalidDataException("The Feature release exceeds 64 MiB.");
        }
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        foreach (var path in files.Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) || relative.Contains("/../", StringComparison.Ordinal))
                throw new InvalidDataException("A Feature release path escaped its root.");
            await UploadImmutableAsync(container.GetBlobClient($"releases/{digest.Value}/{relative}"), path, cancellationToken);
        }
        await UploadImmutableAsync(
            container.GetBlobClient($"metadata/{digest.Value}.json"),
            JsonSerializer.SerializeToUtf8Bytes(metadata),
            MaximumMetadataBytes,
            cancellationToken);
    }
    private static string[] ReleaseFiles(string root)
    {
        var pending = new Queue<string>();
        var files = new List<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("Feature release paths cannot be filesystem links.");
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Enqueue(entry);
                else files.Add(entry);
            }
        }
        return files.ToArray();
    }
    public async Task<FeatureReleaseMetadata> DemandReleaseAsync(ReleaseDigest digest, CancellationToken cancellationToken = default)
    {
        var marker = container.GetBlobClient($"releases/{digest.Value}/digest.txt");
        if (!(await marker.ExistsAsync(cancellationToken)).Value)
            throw new KeyNotFoundException("The verified Feature release is not published.");
        var content = Encoding.UTF8.GetString(await DownloadBoundedAsync(marker, 128, cancellationToken));
        if (!string.Equals(content, digest.Value, StringComparison.Ordinal))
            throw new InvalidDataException("The published Feature release digest marker is invalid.");
        var metadataBytes = await DownloadBoundedAsync(container.GetBlobClient($"metadata/{digest.Value}.json"), MaximumMetadataBytes, cancellationToken);
        var metadata = JsonSerializer.Deserialize<FeatureReleaseMetadata>(metadataBytes)
            ?? throw new InvalidDataException("The published Feature release metadata is invalid.");
        if (metadata.Digest != digest)
            throw new InvalidDataException("The published Feature release metadata has another digest.");
        return metadata;
    }
    public async Task<DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot> DemandSourceAsync(string sourceReference, CancellationToken cancellationToken = default)
    {
        var digest = SourceDigest(sourceReference);
        byte[] bytes;
        try
        {
            bytes = await DownloadBoundedAsync(
                container.GetBlobClient($"sources/{digest}.json"),
                MaximumSerializedSourceBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new KeyNotFoundException("The verified Feature source is not published.", exception);
        }
        var source = JsonSerializer.Deserialize<DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot>(bytes)
            ?? throw new InvalidDataException("The published Feature source snapshot is invalid.");
        DemandSourceReference(sourceReference, source);
        return source;
    }
    private async Task PublishSourceAsync(
        string sourceReference,
        DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot source,
        CancellationToken cancellationToken)
    {
        var digest = SourceDigest(sourceReference);
        DemandSourceReference(sourceReference, source);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await UploadImmutableAsync(
            container.GetBlobClient($"sources/{digest}.json"),
            JsonSerializer.SerializeToUtf8Bytes(source),
            MaximumSerializedSourceBytes,
            cancellationToken).ConfigureAwait(false);
    }
    public async Task<FeaturePublicationReceipt> PublishActiveAsync(
        BrainOwnerId ownerId,
        FeaturePublicationTicket ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var manifest = FeaturePublicationManifestCodec.Serialize(ownerId, ticket);
        var receipt = new FeaturePublicationReceipt(
            ticket.InstallationId,
            ticket.PublicationFence,
            ticket.AuthorityDigest,
            ticket.AccessDigest,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(manifest)));
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        var blob = container.GetBlobClient(FeaturePublicationManifestCodec.Path(ownerId, ticket.InstallationId));
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BlobProperties? properties;
            try
            {
                properties = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Value;
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                properties = null;
            }
            if (properties is null)
            {
                try
                {
                    await blob.UploadAsync(
                        new BinaryData(manifest),
                        new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } },
                        cancellationToken).ConfigureAwait(false);
                    return receipt;
                }
                catch (RequestFailedException exception) when (exception.Status is 409 or 412)
                {
                    continue;
                }
            }
            byte[] existing;
            try
            {
                existing = await DownloadBoundedAsync(blob, properties, MaximumMetadataBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException exception) when (exception.Status is 404 or 412)
            {
                continue;
            }
            var existingFence = PublicationFence(existing);
            if (existingFence > ticket.PublicationFence)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Conflict);
            if (existingFence == ticket.PublicationFence)
            {
                if (existing.AsSpan().SequenceEqual(manifest)) return receipt;
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            }
            try
            {
                await blob.UploadAsync(
                    new BinaryData(manifest),
                    new BlobUploadOptions { Conditions = new BlobRequestConditions { IfMatch = properties.ETag } },
                    cancellationToken).ConfigureAwait(false);
                return receipt;
            }
            catch (RequestFailedException exception) when (exception.Status is 409 or 412)
            {
            }
        }
        throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
    }
    private static async Task UploadImmutableAsync(BlobClient blob, string sourcePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await blob.UploadAsync(source, overwrite: false, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            await VerifyExistingAsync(blob, sourcePath, cancellationToken);
        }
    }
    private static async Task UploadImmutableAsync(
        BlobClient blob,
        byte[] content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Length > maximumBytes)
            throw new InvalidDataException("An immutable Feature artifact exceeds its bound.");
        try
        {
            await blob.UploadAsync(new BinaryData(content), new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            var existing = await DownloadBoundedAsync(blob, maximumBytes, cancellationToken);
            if (!existing.SequenceEqual(content))
                throw new InvalidDataException("An immutable Feature artifact has conflicting content.");
        }
    }
    private static async Task<byte[]> DownloadBoundedAsync(BlobClient blob, int maximumBytes, CancellationToken cancellationToken)
    {
        var properties = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
        return await DownloadBoundedAsync(blob, properties, maximumBytes, cancellationToken).ConfigureAwait(false);
    }
    private static async Task<byte[]> DownloadBoundedAsync(
        BlobClient blob,
        BlobProperties properties,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (properties.ContentLength < 0 || properties.ContentLength > maximumBytes)
            throw new InvalidDataException("A Feature release metadata blob exceeds its bound.");
        var options = new BlobDownloadOptions { Conditions = new BlobRequestConditions { IfMatch = properties.ETag } };
        using var response = (await blob.DownloadStreamingAsync(options, cancellationToken)).Value;
        using var output = new MemoryStream(checked((int)properties.ContentLength));
        var buffer = new byte[4096];
        while (true)
        {
            var read = await response.Content.ReadAsync(buffer, cancellationToken);
            if (read == 0) return output.ToArray();
            if (output.Length + read > maximumBytes)
                throw new InvalidDataException("A Feature release metadata blob exceeds its bound.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static long PublicationFence(byte[] manifest)
    {
        try
        {
            using var document = JsonDocument.Parse(manifest, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The active Feature publication manifest is invalid.");
            if (!root.TryGetProperty("publicationFence", out var fence))
            {
                if (root.TryGetProperty("authorityDigest", out _) || root.TryGetProperty("accessDigest", out _))
                    throw new InvalidDataException("The active Feature publication manifest is invalid.");
                return 0;
            }
            if (!fence.TryGetInt64(out var value) || value < 1 ||
                !root.TryGetProperty("authorityDigest", out var authorityDigest) ||
                !root.TryGetProperty("accessDigest", out var accessDigest) ||
                !CanonicalDigest(authorityDigest) || !CanonicalDigest(accessDigest))
                throw new InvalidDataException("The active Feature publication manifest is invalid.");
            return value;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The active Feature publication manifest is invalid.", exception);
        }
    }

    private static string SourceDigest(string sourceReference)
    {
        if (sourceReference is null || sourceReference.Length != 71 ||
            !sourceReference.StartsWith("sha256:", StringComparison.Ordinal) ||
            sourceReference.Skip(7).Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("A canonical Feature source reference is required.");
        return sourceReference[7..];
    }

    private static void DemandSourceReference(string sourceReference, DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var builderSource = new BuilderFeatureSourceSnapshot(
            source.ImplementationProjectPath,
            source.ScenarioProjectPath,
            source.Files.Select(file => new BuilderFeatureSourceFile(file.Path, file.Content)).ToArray());
        if (!string.Equals(FeatureReleaseWriter.ComputeSourceReference(builderSource), sourceReference, StringComparison.Ordinal))
            throw new InvalidDataException("The Feature source snapshot does not match its content reference.");
    }

    private static bool CanonicalDigest(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        var digest = value.GetString();
        return digest is { Length: 64 } && digest.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
    private static async Task VerifyExistingAsync(BlobClient blob, string sourcePath, CancellationToken cancellationToken)
    {
        var properties = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
        var info = new FileInfo(sourcePath);
        if (properties.ContentLength != info.Length)
            throw new InvalidDataException("An immutable Feature release blob has conflicting content.");
        await using var local = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, true);
        var options = new BlobDownloadOptions { Conditions = new BlobRequestConditions { IfMatch = properties.ETag } };
        using var remote = (await blob.DownloadStreamingAsync(options, cancellationToken)).Value;
        var left = new byte[81_920];
        var right = new byte[81_920];
        while (true)
        {
            var localRead = await local.ReadAsync(left, cancellationToken);
            var remoteRead = await remote.Content.ReadAsync(right, cancellationToken);
            if (localRead != remoteRead || !left.AsSpan(0, localRead).SequenceEqual(right.AsSpan(0, remoteRead)))
                throw new InvalidDataException("An immutable Feature release blob has conflicting content.");
            if (localRead == 0) return;
        }
    }
}
