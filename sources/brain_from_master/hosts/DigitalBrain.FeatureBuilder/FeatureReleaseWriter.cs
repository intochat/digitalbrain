using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace DigitalBrain.FeatureBuilder;

public sealed record FeatureManifest(
    string ImplementationAssembly,
    string SdkVersion,
    IReadOnlyList<string> FeatureTypes,
    IReadOnlyList<string> RequestedCapabilities,
    IReadOnlyList<string> AssemblyReferences);
public enum FeatureScenarioOutcome
{
    Passed,
    Failed,
    Skipped
}
public sealed record FeatureScenarioEvidence(
    string ScenarioId,
    string Name,
    FeatureScenarioOutcome Outcome,
    string? SafeFailure,
    long DurationMilliseconds);
public sealed record FeatureScenarioResult(
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<FeatureScenarioEvidence> Results)
{
    public FeatureScenarioResult(int total, int passed, int failed, int skipped)
        : this(total, passed, failed, skipped, Array.Empty<FeatureScenarioEvidence>())
    {
    }
}
public sealed record FeatureVerificationArtifact(string Name, string MediaType, long SizeBytes, string Digest);
public sealed record FeatureBuildVerification(
    string SourceReference,
    FeatureScenarioResult Scenarios,
    IReadOnlyList<FeatureVerificationArtifact> Artifacts,
    FeatureRelease? Release);
public sealed record FeatureRelease(
    string Digest,
    string SourceReference,
    string ReleaseDirectory,
    FeatureManifest Manifest,
    FeatureScenarioResult Scenarios,
    IReadOnlyList<FeatureVerificationArtifact> Artifacts,
    TimeSpan ReleaseWriteDuration);
public sealed class FeatureReleaseWriter
{
    private const int MaximumVerificationArtifactBytes = 1_048_576;
    public async Task<FeatureRelease> WriteAsync(
        string outputDirectory,
        string sourceReference,
        string buildOutputDirectory,
        FeatureManifest manifest,
        FeatureScenarioResult scenarios,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildOutputDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(scenarios);
        var stopwatch = Stopwatch.StartNew();
        var verificationEntries = VerificationEntries(sourceReference, scenarios);
        var artifacts = Describe(verificationEntries);
        var entries = BuildEntries(buildOutputDirectory, manifest, verificationEntries);
        var digest = ComputeDigest(entries);
        var releaseDirectory = Path.Combine(outputDirectory, digest);
        Directory.CreateDirectory(outputDirectory);
        if (Directory.Exists(releaseDirectory))
        {
            VerifyExisting(releaseDirectory, entries, digest);
            return new FeatureRelease(digest, sourceReference, releaseDirectory, manifest, scenarios, artifacts, stopwatch.Elapsed);
        }
        var staging = Path.Combine(outputDirectory, $".{digest}.{Guid.NewGuid():N}.tmp");
        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(staging, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllBytesAsync(destination, entry.Content, cancellationToken);
            }
            await File.WriteAllTextAsync(Path.Combine(staging, "digest.txt"), digest, new UTF8Encoding(false), cancellationToken);
            try
            {
                Directory.Move(staging, releaseDirectory);
            }
            catch (IOException) when (Directory.Exists(releaseDirectory))
            {
                VerifyExisting(releaseDirectory, entries, digest);
            }
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
        }
        return new FeatureRelease(digest, sourceReference, releaseDirectory, manifest, scenarios, artifacts, stopwatch.Elapsed);
    }
    public static string ComputeSourceReference(FeatureSourceSnapshot snapshot) =>
        DigitalBrain.Shared.FeatureSourceReference.Compute(
            snapshot.ImplementationProjectPath,
            snapshot.ScenarioProjectPath,
            snapshot.Files.Select(static file => (file.Path, file.Content)));
    internal static FeatureVerificationArtifact[] DescribeEvidence(string sourceReference, FeatureScenarioResult scenarios) =>
        Describe(VerificationEntries(sourceReference, scenarios));
    private static ReleaseEntry[] BuildEntries(string buildOutputDirectory, FeatureManifest manifest, IReadOnlyList<ReleaseEntry> verificationEntries)
    {
        var sharedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DigitalBrain.Features.Sdk.dll",
            "DigitalBrain.Integrations.Google.Contracts.dll",
            "DigitalBrain.Integrations.Salesforce.Contracts.dll"
        };
        var outputEntries = Directory.EnumerateFiles(buildOutputDirectory, "*", SearchOption.TopDirectoryOnly).Where(path =>
            {
                var name = Path.GetFileName(path);
                var extension = Path.GetExtension(path);
                return (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) && !sharedAssemblies.Contains(name)) ||
                    name.Equals(Path.ChangeExtension(manifest.ImplementationAssembly, ".deps.json"), StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => new ReleaseEntry("implementation/" + Path.GetFileName(path), File.ReadAllBytes(path)))
            .ToList();
        if (!outputEntries.Any(entry =>
                entry.Path.Equals("implementation/" + manifest.ImplementationAssembly, StringComparison.Ordinal)))
        {
            throw new FeatureBuildException(FeatureBuildFailure.CompilationFailed, "The implementation assembly was not emitted.");
        }
        outputEntries.Add(new ReleaseEntry("manifest.json", ManifestJson(manifest)));
        outputEntries.AddRange(verificationEntries);
        return outputEntries.OrderBy(static entry => entry.Path, StringComparer.Ordinal).ToArray();
    }
    private static ReleaseEntry[] VerificationEntries(string sourceReference, FeatureScenarioResult scenarios) =>
    [
        new ReleaseEntry("scenarios.json", ScenarioJson(scenarios)),
        new ReleaseEntry("source.json", SourceJson(sourceReference))
    ];
    private static FeatureVerificationArtifact[] Describe(IEnumerable<ReleaseEntry> entries) =>
        entries.OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .Select(static entry =>
            {
                if (entry.Content.Length is <= 0 or > MaximumVerificationArtifactBytes)
                {
                    throw new FeatureBuildException(FeatureBuildFailure.ScenarioFailed, "A verification artifact exceeded its bound.");
                }
                return new FeatureVerificationArtifact(
                    entry.Path,
                    "application/json",
                    entry.Content.LongLength,
                    "sha256:" + Convert.ToHexStringLower(SHA256.HashData(entry.Content)));
            })
            .ToArray();
    private static byte[] ManifestJson(FeatureManifest manifest) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("implementationAssembly", manifest.ImplementationAssembly);
        writer.WriteString("sdkVersion", manifest.SdkVersion);
        WriteArray(writer, "featureTypes", manifest.FeatureTypes);
        WriteArray(writer, "requestedCapabilities", manifest.RequestedCapabilities);
        WriteArray(writer, "assemblyReferences", manifest.AssemblyReferences);
        writer.WriteEndObject();
    });
    private static byte[] ScenarioJson(FeatureScenarioResult scenarios) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("total", scenarios.Total);
        writer.WriteNumber("passed", scenarios.Passed);
        writer.WriteNumber("failed", scenarios.Failed);
        writer.WriteNumber("skipped", scenarios.Skipped);
        writer.WriteStartArray("results");
        foreach (var result in scenarios.Results)
        {
            writer.WriteStartObject();
            writer.WriteString("scenarioId", result.ScenarioId);
            writer.WriteString("name", result.Name);
            writer.WriteString("outcome", result.Outcome.ToString().ToLowerInvariant());
            if (result.SafeFailure is not null)
            {
                writer.WriteString("safeFailure", result.SafeFailure);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    });
    private static byte[] SourceJson(string sourceReference) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("reference", sourceReference);
        writer.WriteEndObject();
    });
    private static byte[] WriteJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            write(writer);
        }
        return stream.ToArray();
    }
    private static void WriteArray(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }
    private static string ComputeDigest(IEnumerable<ReleaseEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (var entry in entries.OrderBy(static entry => entry.Path, StringComparer.Ordinal))
        {
            var path = Encoding.UTF8.GetBytes(entry.Path);
            BinaryPrimitives.WriteInt64BigEndian(length, path.Length);
            hash.AppendData(length);
            hash.AppendData(path);
            BinaryPrimitives.WriteInt64BigEndian(length, entry.Content.Length);
            hash.AppendData(length);
            hash.AppendData(entry.Content);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static void VerifyExisting(string releaseDirectory, IReadOnlyList<ReleaseEntry> entries, string digest)
    {
        var expectedPaths = entries.Select(static entry => entry.Path)
            .Append("digest.txt")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualPaths = Directory.EnumerateFiles(releaseDirectory, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(releaseDirectory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!expectedPaths.SequenceEqual(actualPaths, StringComparer.Ordinal))
        {
            throw new FeatureBuildException(FeatureBuildFailure.ReleaseConflict, $"Existing release '{digest}' has unexpected files.");
        }
        foreach (var entry in entries)
        {
            var path = Path.Combine(releaseDirectory, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(entry.Content))
            {
                throw new FeatureBuildException(FeatureBuildFailure.ReleaseConflict, $"Existing release '{digest}' does not match its content address.");
            }
        }
        var digestPath = Path.Combine(releaseDirectory, "digest.txt");
        if (!File.Exists(digestPath) || !string.Equals(File.ReadAllText(digestPath), digest, StringComparison.Ordinal))
        {
            throw new FeatureBuildException(FeatureBuildFailure.ReleaseConflict, $"Existing release '{digest}' has an invalid digest marker.");
        }
    }
    private sealed record ReleaseEntry(string Path, byte[] Content);
}
