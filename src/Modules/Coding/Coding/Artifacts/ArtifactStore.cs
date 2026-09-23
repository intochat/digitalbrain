using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Coding;

public sealed class ArtifactStore : ICodeArtifactStore
{
    private readonly string _root;
    private readonly Func<BuildEnvironment> _environment;
    private readonly long _maximumBytes;

    public ArtifactStore(string root, BuildEnvironment environment, long maximumBytes = 1024L * 1024 * 1024)
        : this(root, () => environment, maximumBytes) { }

    public ArtifactStore(string root, Func<BuildEnvironment> environment, long maximumBytes = 1024L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _root = Path.GetFullPath(root);
        _environment = environment;
        _maximumBytes = maximumBytes;
        Directory.CreateDirectory(_root);
        RejectLinks(_root);
    }

    public async Task<CodeArtifactRef> SealAsync(VerifiedBuild build, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireReport(build.Report);
        if (EnvironmentHash(build.Environment) != EnvironmentHash(_environment()))
        { throw new InvalidDataException("Build environment does not match the current host."); }
        var payload = Path.GetFullPath(build.PayloadDirectory);
        var files = await HashFiles(payload, cancellationToken).ConfigureAwait(false);
        if (!files.ContainsKey(build.EntryAssembly)) { throw new InvalidDataException("Entry assembly is missing."); }
        var manifest = new ArtifactManifest(build.Source, build.Tests, Canonical(build.Environment), build.EntryAssembly, build.Report, files);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var id = Hash(bytes);
        var reference = new CodeArtifactRef(id, Hash(Encoding.UTF8.GetBytes(build.Source)), EnvironmentHash(build.Environment));
        await using var lease = await AcquireWriteLease(cancellationToken).ConfigureAwait(false);
        var temporary = Path.Combine(_root, ".pending-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (Directory.Exists(Path.Combine(_root, id)))
            { await OpenVerifiedAsync(reference, cancellationToken).ConfigureAwait(false); return reference; }
            var existingBytes = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
            var candidateBytes = files.Keys.Sum(p => new FileInfo(Path.Combine(payload, p)).Length) + bytes.LongLength;
            if (existingBytes > _maximumBytes - candidateBytes) { throw new IOException("Artifact storage quota exceeded."); }
            var targetPayload = Path.Combine(temporary, "payload");
            Directory.CreateDirectory(targetPayload);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(targetPayload, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(payload, file.Key), target);
            }
            var copied = await HashFiles(targetPayload, cancellationToken).ConfigureAwait(false);
            if (!files.SequenceEqual(copied)) { throw new InvalidDataException("Payload changed while sealing."); }
            await File.WriteAllBytesAsync(Path.Combine(temporary, "manifest.json"), bytes, cancellationToken).ConfigureAwait(false);
            Directory.Move(temporary, Path.Combine(_root, id));
            return reference;
        }
        finally
        {
            if (Directory.Exists(temporary)) { Directory.Delete(temporary, true); }
        }
    }

    public async Task<VerifiedArtifact> OpenVerifiedAsync(CodeArtifactRef reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Id.Length != 64 || reference.Id.Any(c => !char.IsAsciiHexDigit(c)))
        { throw new ArgumentException("An artifact ID must be a SHA-256 digest.", nameof(reference)); }
        var directory = Path.Combine(_root, reference.Id);
        RejectLinks(directory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        RejectLinks(manifestPath);
        if (new FileInfo(manifestPath).Length > 1024 * 1024) { throw new InvalidDataException("Artifact manifest exceeds its limit."); }
        var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (Hash(bytes) != reference.Id) { throw new InvalidDataException("Artifact manifest integrity check failed."); }
        var manifest = JsonSerializer.Deserialize<ArtifactManifest>(bytes) ?? throw new InvalidDataException("Missing manifest.");
        RequireReport(manifest.Report);
        if (Hash(Encoding.UTF8.GetBytes(manifest.Source)) != reference.SourceHash
            || EnvironmentHash(manifest.Environment) != reference.EnvironmentHash
            || reference.EnvironmentHash != EnvironmentHash(_environment()))
        { throw new InvalidDataException("Artifact source or environment does not match."); }
        var payload = Path.Combine(directory, "payload");
        var files = await HashFiles(payload, cancellationToken).ConfigureAwait(false);
        if (!files.SequenceEqual(manifest.Files.OrderBy(x => x.Key, StringComparer.Ordinal)) || !files.ContainsKey(manifest.EntryAssembly))
        { throw new InvalidDataException("Artifact payload integrity check failed."); }
        return new(reference, manifest, payload, manifest.EntryAssembly);
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string EnvironmentHash(BuildEnvironment environment) => Hash(JsonSerializer.SerializeToUtf8Bytes(Canonical(environment)));
    private static BuildEnvironment Canonical(BuildEnvironment environment)
        => environment with { Assemblies = new SortedDictionary<string, string>(environment.Assemblies.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal) };

    private static void RequireReport(CodeTestReport report)
    {
        if (report.Discovered <= 0 || report.Passed != report.Discovered || report.Failed != 0 || report.Skipped != 0 || string.IsNullOrWhiteSpace(report.ReportHash))
        { throw new InvalidDataException("A complete passing test report is required."); }
    }

    internal static async Task<SortedDictionary<string, string>> HashFiles(string directory, CancellationToken cancellationToken)
    {
        RejectLinks(directory);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectLinks(path);
            if (Directory.Exists(path))
            {
                var children = await HashFiles(path, cancellationToken).ConfigureAwait(false);
                foreach (var child in children) { result.Add(Path.GetFileName(path) + "/" + child.Key, child.Value); }
            }
            else
            {
                await using var file = File.OpenRead(path);
                var hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
                result.Add(Path.GetFileName(path), Convert.ToHexStringLower(hash));
            }
        }
        return result;
    }

    private async Task<FileStream> AcquireWriteLease(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(_root, "seal.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            { await Task.Delay(20, ct).ConfigureAwait(false); }
        }
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new InvalidDataException("Artifact paths cannot traverse symbolic links or reparse points."); }
        }
    }
}