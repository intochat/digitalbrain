using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.FeatureBuilder;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Mcp;

public sealed record FeatureSourceInput(string Path, string Content);

public sealed record FeatureBuildSubmission(string ImplementationProjectPath, string ScenarioProjectPath, IReadOnlyList<FeatureSourceInput> Files, FeatureSourceKind SourceKind);

public sealed record FeatureBuildArtifact(FeatureReleaseMetadata Release, FeatureScenarioResult Scenarios);

public sealed class FeatureBuildEndpoint(FeatureArtifactPublisher artifacts, TimeProvider timeProvider)
{
    private const int MaximumProcessOutputCharacters = 65_536;

    public async Task<FeatureBuildArtifact> BuildAsync(FeatureBuildSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(submission.Files);
        var source = new FeatureSourceSnapshot(
            submission.ImplementationProjectPath,
            submission.ScenarioProjectPath,
            submission.Files.Select(file => new FeatureSourceFile(file.Path, file.Content)).ToArray());
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
            var release = await RunBuilderAsync(requestPath, cancellationToken);
            if (!string.Equals(Path.GetFullPath(release.ReleaseDirectory), Path.GetFullPath(Path.Combine(output, release.Digest)), PathComparison))
                throw new InvalidDataException("FeatureBuilder returned a release outside its assigned output.");
            var metadata = new FeatureReleaseMetadata(
                new ReleaseDigest(release.Digest),
                release.SourceReference,
                submission.SourceKind,
                release.Manifest.RequestedCapabilities.ToArray(),
                release.Manifest.AssemblyReferences.ToArray());
            await artifacts.PublishReleaseAsync(metadata, release.ReleaseDirectory, cancellationToken);
            return new FeatureBuildArtifact(metadata, release.Scenarios);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<BuilderRelease> RunBuilderAsync(string requestPath, CancellationToken cancellationToken)
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
        return JsonSerializer.Deserialize<BuilderRelease>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("FeatureBuilder returned no release.");
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

    private sealed record BuilderRelease(string Digest, string SourceReference, string ReleaseDirectory, FeatureManifest Manifest, FeatureScenarioResult Scenarios);
}

public sealed class FeatureArtifactPublisher([FromKeyedServices("features")] BlobServiceClient blobs)
{
    private const string ContainerName = "feature-releases";
    private const int MaximumReleaseFiles = 256;
    private const long MaximumReleaseBytes = 67_108_864;
    private const int MaximumMetadataBytes = 65_536;
    private readonly BlobContainerClient container = blobs.GetBlobContainerClient(ContainerName);

    public async Task PublishReleaseAsync(FeatureReleaseMetadata metadata, string releaseDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!Enum.IsDefined(metadata.SourceKind))
            throw new ArgumentException("The Feature source kind is invalid.", nameof(metadata));
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
        await UploadImmutableAsync(container.GetBlobClient($"metadata/{digest.Value}.json"), JsonSerializer.SerializeToUtf8Bytes(metadata), cancellationToken);
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

    public async Task PublishActiveAsync(BrainOwnerId ownerId, FeatureAuthoritySnapshot authority, CancellationToken cancellationToken = default)
    {
        if (authority.ActiveRelease is not { } release || authority.ActiveGrantRevision is not { } revision)
            throw new InvalidOperationException("Only an active Feature authority can be published.");
        var connections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var grant in authority.ActiveGrants)
        {
            if (grant.ProviderConnectionId is not { } connection) continue;
            var provider = grant.Provider ?? throw new InvalidOperationException("A provider connection requires a provider key.");
            if (!connections.TryAdd(provider, connection.Value) &&
                !string.Equals(connections[provider], connection.Value, StringComparison.Ordinal))
                throw new InvalidOperationException("One installation cannot bind a provider to multiple connections.");
        }
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ownerId = ownerId.Value,
            actorId = authority.ActorId.Value,
            installationId = authority.InstallationId.Value,
            releaseDigest = release.Value,
            grantRevision = revision.Value,
            providerConnections = connections
        });
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await container.GetBlobClient($"active/{Segment(ownerId.Value)}/{Segment(authority.InstallationId.Value)}.json").UploadAsync(new BinaryData(manifest), overwrite: true, cancellationToken);
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

    private static async Task UploadImmutableAsync(BlobClient blob, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            await blob.UploadAsync(new BinaryData(content), new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            var existing = await DownloadBoundedAsync(blob, MaximumMetadataBytes, cancellationToken);
            if (!existing.SequenceEqual(content))
                throw new InvalidDataException("An immutable Feature release metadata blob has conflicting content.");
        }
    }

    private static async Task<byte[]> DownloadBoundedAsync(BlobClient blob, int maximumBytes, CancellationToken cancellationToken)
    {
        var properties = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
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

    private static string Segment(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
