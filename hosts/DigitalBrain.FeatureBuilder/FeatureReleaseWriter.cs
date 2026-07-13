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

public sealed record FeatureScenarioResult(int Total, int Passed, int Failed, int Skipped);

public sealed record FeatureRelease(
    string Digest,
    string SourceReference,
    string ReleaseDirectory,
    FeatureManifest Manifest,
    FeatureScenarioResult Scenarios,
    TimeSpan ReleaseWriteDuration);

public sealed class FeatureReleaseWriter
{
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
        var entries = BuildEntries(sourceReference, buildOutputDirectory, manifest, scenarios);
        var digest = ComputeDigest(entries);
        var releaseDirectory = Path.Combine(outputDirectory, digest);
        Directory.CreateDirectory(outputDirectory);
        if (Directory.Exists(releaseDirectory))
        {
            VerifyExisting(releaseDirectory, entries, digest);
            return new FeatureRelease(
                digest,
                sourceReference,
                releaseDirectory,
                manifest,
                scenarios,
                stopwatch.Elapsed);
        }

        var staging = Path.Combine(outputDirectory, $".{digest}.{Guid.NewGuid():N}.tmp");
        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(
                    staging,
                    entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllBytesAsync(destination, entry.Content, cancellationToken);
            }

            await File.WriteAllTextAsync(
                Path.Combine(staging, "digest.txt"),
                digest,
                new UTF8Encoding(false),
                cancellationToken);
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

        return new FeatureRelease(
            digest,
            sourceReference,
            releaseDirectory,
            manifest,
            scenarios,
            stopwatch.Elapsed);
    }

    internal static string ComputeSourceReference(FeatureSourceSnapshot snapshot)
    {
        var entries = snapshot.Files
            .Select(static file => new ReleaseEntry(
                "files/" + file.Path,
                Encoding.UTF8.GetBytes(file.Content)))
            .Append(new ReleaseEntry(
                "entries/implementation",
                Encoding.UTF8.GetBytes(snapshot.ImplementationProjectPath)))
            .Append(new ReleaseEntry(
                "entries/scenarios",
                Encoding.UTF8.GetBytes(snapshot.ScenarioProjectPath)))
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
        return "sha256:" + ComputeDigest(entries);
    }

    private static ReleaseEntry[] BuildEntries(
        string sourceReference,
        string buildOutputDirectory,
        FeatureManifest manifest,
        FeatureScenarioResult scenarios)
    {
        var sharedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DigitalBrain.Features.Sdk.dll",
            "DigitalBrain.Integrations.Google.Contracts.dll",
            "DigitalBrain.Integrations.Salesforce.Contracts.dll"
        };
        var outputEntries = Directory.EnumerateFiles(buildOutputDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                var extension = Path.GetExtension(path);
                return (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                        !sharedAssemblies.Contains(name)) ||
                    name.Equals(
                        Path.ChangeExtension(manifest.ImplementationAssembly, ".deps.json"),
                        StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => new ReleaseEntry(
                "implementation/" + Path.GetFileName(path),
                File.ReadAllBytes(path)))
            .ToList();
        if (!outputEntries.Any(entry =>
                entry.Path.Equals(
                    "implementation/" + manifest.ImplementationAssembly,
                    StringComparison.Ordinal)))
        {
            throw new FeatureBuildException(
                FeatureBuildFailure.CompilationFailed,
                "The implementation assembly was not emitted.");
        }

        outputEntries.Add(new ReleaseEntry("manifest.json", ManifestJson(manifest)));
        outputEntries.Add(new ReleaseEntry("scenarios.json", ScenarioJson(scenarios)));
        outputEntries.Add(new ReleaseEntry("source.json", SourceJson(sourceReference)));
        return outputEntries.OrderBy(static entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

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

    private static void WriteArray(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<string> values)
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

    private static void VerifyExisting(
        string releaseDirectory,
        IReadOnlyList<ReleaseEntry> entries,
        string digest)
    {
        var expectedPaths = entries
            .Select(static entry => entry.Path)
            .Append("digest.txt")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualPaths = Directory.EnumerateFiles(releaseDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(releaseDirectory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!expectedPaths.SequenceEqual(actualPaths, StringComparer.Ordinal))
        {
            throw new FeatureBuildException(
                FeatureBuildFailure.ReleaseConflict,
                $"Existing release '{digest}' has unexpected files.");
        }

        foreach (var entry in entries)
        {
            var path = Path.Combine(
                releaseDirectory,
                entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(entry.Content))
            {
                throw new FeatureBuildException(
                    FeatureBuildFailure.ReleaseConflict,
                    $"Existing release '{digest}' does not match its content address.");
            }
        }

        var digestPath = Path.Combine(releaseDirectory, "digest.txt");
        if (!File.Exists(digestPath) ||
            !string.Equals(File.ReadAllText(digestPath), digest, StringComparison.Ordinal))
        {
            throw new FeatureBuildException(
                FeatureBuildFailure.ReleaseConflict,
                $"Existing release '{digest}' has an invalid digest marker.");
        }
    }

    private sealed record ReleaseEntry(string Path, byte[] Content);
}
