using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Apps.Packaging;

public sealed class AppPackageReader(PackageLimits? limits = null, IReadOnlyDictionary<string, string>? sharedAssemblies = null)
{
    private readonly PackageLimits limits = limits ?? new();
    private readonly IReadOnlyDictionary<string, string> sharedAssemblies = sharedAssemblies ?? new Dictionary<string, string>();

    public async Task<ValidatedAppPackage> Read(Stream archive, string stagingRoot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        var temporary = System.IO.Path.GetTempFileName();
        string? candidate = null;
        try
        {
            await using var copy = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
            await CopyBounded(archive, copy, limits.MaxArchiveBytes, ct).ConfigureAwait(false);
            copy.Position = 0;
            var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(copy, ct).ConfigureAwait(false));
            copy.Position = 0;
            using var zip = new ZipArchive(copy, ZipArchiveMode.Read, true);
            if (zip.Entries.Count > limits.MaxEntries) { throw new InvalidDataException("Too many package entries."); }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                PackageFormat.Path(entry.FullName);
                if (!names.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                { throw new InvalidDataException("Duplicate path or link in archive."); }
                if (entry.Length > limits.MaxExpandedBytes - expanded) { throw new InvalidDataException("Expanded package limit exceeded."); }
                expanded += entry.Length;
            }
            var manifest = zip.GetEntry("app.json") ?? throw new InvalidDataException("Package metadata missing.");
            CompiledAppDescriptor descriptor;
            await using (var input = manifest.Open())
            {
                using var metadata = new MemoryStream();
                await CopyBounded(input, metadata, limits.MaxManifestBytes, ct).ConfigureAwait(false);
                descriptor = JsonSerializer.Deserialize<CompiledAppDescriptor>(metadata.ToArray(), PackageFormat.Json) ?? throw new InvalidDataException("Empty metadata.");
            }
            PackageFormat.Validate(descriptor, sharedAssemblies);
            if (zip.Entries.Count != descriptor.Payload.Length + 1) { throw new InvalidDataException("Payload inventory does not match archive."); }
            var boundary = new DirectoryInfo(stagingRoot);
            if (boundary.Exists && (boundary.Attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Staging boundary is a link."); }
            Directory.CreateDirectory(stagingRoot);
            candidate = System.IO.Path.Combine(stagingRoot, ".candidate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(candidate);
            long actualExpanded = manifest.Length;
            foreach (var payload in descriptor.Payload)
            {
                var entry = zip.GetEntry(payload.Path) ?? throw new InvalidDataException("Inventory payload missing.");
                if (entry.Length != payload.Length) { throw new InvalidDataException("Payload length mismatch."); }
                var path = System.IO.Path.Combine(candidate, payload.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await using var input = entry.Open();
                    var length = await CopyBounded(input, target, Math.Min(payload.Length, limits.MaxExpandedBytes - actualExpanded), ct).ConfigureAwait(false);
                    actualExpanded += length;
                    target.Position = 0;
                    var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(target, ct).ConfigureAwait(false));
                    if (length != payload.Length || !hash.Equals(payload.Sha256, StringComparison.Ordinal)) { throw new InvalidDataException("Payload hash mismatch."); }
                    target.Position = 0;
                    PackageFormat.PayloadFile(payload.Path, target);
                }
            }
            await File.WriteAllBytesAsync(System.IO.Path.Combine(candidate, "app.json"), JsonSerializer.SerializeToUtf8Bytes(PackageFormat.Canonical(descriptor), PackageFormat.Json), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var destination = System.IO.Path.Combine(stagingRoot, digest);
            // Verify content again when reusing an artifact; never overwrite a generation.
            if (Directory.Exists(destination))
            {
                await VerifyExisting(destination, candidate, ct).ConfigureAwait(false);
                Directory.Delete(candidate, true);
                candidate = null;
            }
            else
            {
                foreach (var file in Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories))
                { File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly); }
                Directory.Move(candidate, destination);
                candidate = null;
            }
            return new(descriptor, digest, destination);
        }
        catch (JsonException exception) { throw new InvalidDataException("Invalid package metadata.", exception); }
        finally
        {
            if (candidate is not null)
            {
                foreach (var file in Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories)) { File.SetAttributes(file, FileAttributes.Normal); }
                Directory.Delete(candidate, true);
            }
            File.Delete(temporary);
        }
    }

    private static async Task VerifyExisting(string destination, string candidate, CancellationToken ct)
    {
        if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Stored artifact is a link."); }
        var expected = Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories).Select(f => System.IO.Path.GetRelativePath(candidate, f)).Order(StringComparer.Ordinal).ToArray();
        var actual = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Select(f => System.IO.Path.GetRelativePath(destination, f)).Order(StringComparer.Ordinal).ToArray();
        foreach (var directory in Directory.EnumerateDirectories(destination, "*", SearchOption.AllDirectories))
        { if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Stored artifact contains a link."); } }
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal)) { throw new InvalidDataException("Stored artifact inventory changed."); }
        foreach (var path in expected)
        {
            var stored = System.IO.Path.Combine(destination, path);
            if ((File.GetAttributes(stored) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Stored artifact contains a link."); }
            await using var left = File.OpenRead(stored);
            await using var right = File.OpenRead(System.IO.Path.Combine(candidate, path));
            var leftHash = await SHA256.HashDataAsync(left, ct).ConfigureAwait(false);
            var rightHash = await SHA256.HashDataAsync(right, ct).ConfigureAwait(false);
            if (!leftHash.AsSpan().SequenceEqual(rightHash))
            { throw new InvalidDataException("Stored artifact content changed."); }
        }
    }

    private static async Task<long> CopyBounded(Stream source, Stream target, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (read > limit - total) { throw new InvalidDataException("Package stream limit exceeded."); }
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            total += read;
        }
        return total;
    }
}
