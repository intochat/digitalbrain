using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.FeatureHost;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.UnitTests;

internal sealed class FeatureReleaseTestArtifact : IDisposable
{
    private FeatureReleaseTestArtifact(string releaseDirectory, string digest)
    {
        ReleaseDirectory = releaseDirectory;
        Descriptor = new FeatureReleaseDescriptor(new ReleaseDigest(digest), releaseDirectory);
    }

    public string ReleaseDirectory { get; }
    public string ImplementationAssemblyPath => Path.Combine(
        ReleaseDirectory,
        "implementation",
        "DigitalBrain.Features.EmailSummarizer.dll");
    public FeatureReleaseDescriptor Descriptor { get; }

    public static FeatureReleaseTestArtifact Create(
        string sourceReference = "sha256:test-source",
        string? sdkVersion = null,
        int scenarioFailures = 0,
        int scenarioSkips = 0,
        string featureTypeName = "DigitalBrain.Features.EmailSummarizer.EmailSummarizerFeature")
    {
        var root = RepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new DirectoryNotFoundException();
        var source = Path.Combine(
            root,
            "features",
            "EmailSummarizer",
            "bin",
            configuration,
            "net11.0");
        var release = Directory.CreateTempSubdirectory("digitalbrain-feature-release-").FullName;
        var implementation = Directory.CreateDirectory(Path.Combine(release, "implementation")).FullName;
        Copy(source, implementation, "DigitalBrain.Features.EmailSummarizer.dll");
        Copy(source, implementation, "DigitalBrain.Features.EmailSummarizer.deps.json");
        File.WriteAllBytes(Path.Combine(release, "manifest.json"), Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("implementationAssembly", "DigitalBrain.Features.EmailSummarizer.dll");
            writer.WriteString(
                "sdkVersion",
                sdkVersion ?? typeof(IFeature).Assembly.GetName().Version!.ToString());
            writer.WriteStartArray("featureTypes");
            writer.WriteStringValue(featureTypeName);
            writer.WriteEndArray();
            writer.WriteStartArray("requestedCapabilities");
            writer.WriteStringValue("google.gmail.message.read.v1");
            writer.WriteEndArray();
            writer.WriteStartArray("assemblyReferences");
            writer.WriteStringValue("DigitalBrain.Features.Sdk");
            writer.WriteStringValue("DigitalBrain.Integrations.Google.Contracts");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }));
        File.WriteAllBytes(Path.Combine(release, "scenarios.json"), Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("total", 4);
            writer.WriteNumber("passed", 4 - scenarioFailures - scenarioSkips);
            writer.WriteNumber("failed", scenarioFailures);
            writer.WriteNumber("skipped", scenarioSkips);
            writer.WriteEndObject();
        }));
        File.WriteAllBytes(Path.Combine(release, "source.json"), Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("reference", sourceReference);
            writer.WriteEndObject();
        }));
        var digest = ComputeDigest(release);
        File.WriteAllText(Path.Combine(release, "digest.txt"), digest, new UTF8Encoding(false));
        return new FeatureReleaseTestArtifact(release, digest);
    }

    public void Dispose()
    {
        if (Directory.Exists(ReleaseDirectory))
            Directory.Delete(ReleaseDirectory, recursive: true);
    }

    private static void Copy(string source, string destination, string fileName)
    {
        var path = Path.Combine(source, fileName);
        if (File.Exists(path))
            File.Copy(path, Path.Combine(destination, fileName));
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            write(writer);
        return stream.ToArray();
    }

    private static string ComputeDigest(string releaseDirectory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (var path in Directory.EnumerateFiles(releaseDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => !Path.GetFileName(path).Equals("digest.txt", StringComparison.Ordinal))
                     .OrderBy(path => Path.GetRelativePath(releaseDirectory, path).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var relativePath = Encoding.UTF8.GetBytes(
                Path.GetRelativePath(releaseDirectory, path).Replace('\\', '/'));
            var content = File.ReadAllBytes(path);
            BinaryPrimitives.WriteInt64BigEndian(length, relativePath.Length);
            hash.AppendData(length);
            hash.AppendData(relativePath);
            BinaryPrimitives.WriteInt64BigEndian(length, content.Length);
            hash.AppendData(length);
            hash.AppendData(content);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
