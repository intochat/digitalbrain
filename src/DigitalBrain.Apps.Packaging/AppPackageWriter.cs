using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Apps.Packaging;

public sealed class AppPackageWriter
{
    private readonly PackageLimits limits;

    public AppPackageWriter(PackageLimits? limits = null) => this.limits = limits ?? new();

    public async Task<string> Write(CompiledAppDescriptor descriptor, string buildDirectory, Stream output, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(output);
        var root = new DirectoryInfo(buildDirectory);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Collect(root, root, files);
        foreach (var path in files.Keys.Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && descriptor.SharedAssemblies.ContainsKey(System.IO.Path.GetFileNameWithoutExtension(path))).ToArray())
        { files.Remove(path); }
        var inventory = new List<PackagePayload>();
        foreach (var pair in files)
        {
            ct.ThrowIfCancellationRequested();
            await using var file = File.OpenRead(pair.Value);
            PackageFormat.PayloadFile(pair.Key, file);
            file.Position = 0;
            inventory.Add(new(pair.Key, file.Length, Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false))));
        }
        descriptor = PackageFormat.Canonical(descriptor with { Payload = [.. inventory] });
        PackageFormat.Validate(descriptor, descriptor.SharedAssemblies);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(descriptor, PackageFormat.Json);
        if (inventory.Count + 1 > limits.MaxEntries || manifest.LongLength > limits.MaxManifestBytes || inventory.Sum(item => item.Length) > limits.MaxExpandedBytes - manifest.LongLength)
        { throw new InvalidDataException("Package exceeds configured limits."); }
        var temporary = System.IO.Path.GetTempFileName();
        try
        {
            await using var archive = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
            {
                await using (var metadata = Entry(zip, "app.json").Open())
                { await metadata.WriteAsync(manifest, ct).ConfigureAwait(false); }
                foreach (var pair in files)
                {
                    await using var source = File.OpenRead(pair.Value);
                    await using var target = Entry(zip, pair.Key).Open();
                    await source.CopyToAsync(target, ct).ConfigureAwait(false);
                }
            }
            archive.Position = 0;
            if (archive.Length > limits.MaxArchiveBytes) { throw new InvalidDataException("Archive limit exceeded."); }
            var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(archive, ct).ConfigureAwait(false));
            archive.Position = 0;
            await archive.CopyToAsync(output, ct).ConfigureAwait(false);
            return digest;
        }
        finally { File.Delete(temporary); }
    }

    private static ZipArchiveEntry Entry(ZipArchive zip, string path)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        entry.ExternalAttributes = 0;
        return entry;
    }

    private static void Collect(DirectoryInfo root, DirectoryInfo directory, SortedDictionary<string, string> files)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Build directory links are unsupported."); }
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Build links are unsupported."); }
            var relative = System.IO.Path.GetRelativePath(root.FullName, entry.FullName).Replace('\\', '/');
            if (PackageFormat.Excluded(relative)) { continue; }
            if (entry is DirectoryInfo child) { Collect(root, child, files); }
            else
            {
                PackageFormat.Path(relative);
                if (relative.Equals("app.json", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (files.Keys.Contains(relative, StringComparer.OrdinalIgnoreCase)) { throw new InvalidDataException("Case-colliding payload paths."); }
                files.Add(relative, entry.FullName);
            }
        }
    }
}
