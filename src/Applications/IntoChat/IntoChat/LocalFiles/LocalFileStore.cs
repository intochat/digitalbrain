using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Flutter.Collection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace IntoChat.LocalFiles;

internal sealed class LocalFileStore(IOptions<LocalFilesOptions> options, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("IntoChat.LocalFiles.v1");
    private readonly LocalFilesOptions _options = options.Value;

    public Task<DirectoryPage> ListAsync(string scope, string? folderId, int offset, string sort, string filter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (offset < 0 || offset > 100000 || filter.Length > 200) { throw new ArgumentException("Invalid file listing request."); }
        var handle = folderId is null ? new FileHandle(scope, "downloads", "") : Decode(scope, folderId);
        var folder = Resolve(handle);
        using var lease = LocalPathLease.Acquire(folder);
        var entries = new DirectoryInfo(folder).EnumerateFileSystemInfos().Where(x => !x.Attributes.HasFlag(FileAttributes.ReparsePoint) && !x.Name.StartsWith(".intochat-", StringComparison.Ordinal) && x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        // Enumerate only this directory. Sorting is bounded to prevent unbounded metadata allocations.
        var bounded = entries.Take(100001).ToArray();
        if (bounded.Length > 100000) { throw new IOException("This folder contains too many entries. Open a smaller folder."); }
        var ordered = sort switch
        {
            "date" => bounded.OrderByDescending(x => x.LastWriteTimeUtc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "size" => bounded.OrderByDescending(x => x is FileInfo f ? f.Length : 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            _ => bounded.OrderByDescending(x => x is DirectoryInfo).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        };
        var rows = ordered.Skip(offset).Take(101).Select(x => new CollectionItem(
            Encode(handle with { RelativePath = Path.Combine(handle.RelativePath, x.Name), Length = x is FileInfo version ? version.Length : null, ModifiedTicks = x is FileInfo ? x.LastWriteTimeUtc.Ticks : null, CreatedTicks = x is FileInfo ? x.CreationTimeUtc.Ticks : null }), x.Name,
            x is DirectoryInfo ? "folder" : IsImage(x.Name) ? "image" : "file", null,
            x is FileInfo file ? file.Length : null, new DateTimeOffset(x.LastWriteTimeUtc), x is DirectoryInfo || IsImage(x.Name))).ToArray();
        var crumbs = new List<DirectoryCrumb> { new(Encode(new(scope, handle.Root, "")), "Downloads") };
        var relativeCrumb = "";
        foreach (var part in handle.RelativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            relativeCrumb = Path.Combine(relativeCrumb, part);
            crumbs.Add(new(Encode(new(scope, handle.Root, relativeCrumb)), part));
        }
        return Task.FromResult(new DirectoryPage(Encode(handle), handle.RelativePath.Length == 0 ? "Downloads" : Path.GetFileName(folder),
            handle.RelativePath.Length == 0 ? null : Encode(handle with { RelativePath = Path.GetDirectoryName(handle.RelativePath) ?? "" }),
            rows.Take(100).ToArray(), rows.Length > 100 ? offset + 100 : null, crumbs));
    }

    public async Task<ImageAsset> SnapshotImageAsync(string scope, string entryId, CancellationToken ct)
    {
        var handle = Decode(scope, entryId);
        var path = Resolve(handle);
        if (!IsImage(path)) { throw new ArgumentException("Only PNG and JPEG images can be opened."); }
        using var lease = LocalPathLease.Acquire(Path.GetDirectoryName(path)!);
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        LocalPathLease.Verify(source.SafeFileHandle, path);
        var current = new FileInfo(path);
        if (handle.Length is not null && (handle.Length != source.Length || handle.ModifiedTicks != current.LastWriteTimeUtc.Ticks || handle.CreatedTicks != current.CreationTimeUtc.Ticks))
        { throw new IOException("This file changed since it was listed. Refresh Files and open it again."); }
        if (source.Length > LocalFilesOptions.MaxSourceBytes) { throw new ArgumentException("Choose an image smaller than 32 MiB."); }
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > LocalFilesOptions.MaxSourceBytes) { throw new ArgumentException("Choose an image smaller than 32 MiB."); }
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (buffer.Length > LocalFilesOptions.MaxSourceBytes) { throw new ArgumentException("Choose an image smaller than 32 MiB."); }
        var bytes = buffer.ToArray();
        var (width, height) = ImageHeader.Read(bytes);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var id = ScopeKey(scope) + "-" + digest;
        var destination = AssetPath(scope, id);
        Directory.CreateDirectory(_options.AssetDirectory);
        var pending = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(pending, bytes, ct);
            try { File.Move(pending, destination, false); }
            catch (IOException) when (File.Exists(destination))
            {
                var existing = await File.ReadAllBytesAsync(destination, ct);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing), SHA256.HashData(bytes))) { throw new IOException("The image snapshot is incomplete. Retry opening the image."); }
            }
        }
        finally { if (File.Exists(pending)) { File.Delete(pending); } }
        var identity = scope + "\0" + handle.Root + "\0" + (OperatingSystem.IsWindows() ? handle.RelativePath.ToUpperInvariant() : handle.RelativePath) + "\0" + digest;
        var documentId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new(id, Path.GetFileName(path), width, height, digest, entryId, documentId);
    }

    public Stream OpenAsset(string scope, string assetId) => new FileStream(AssetPath(scope, assetId), FileMode.Open, FileAccess.Read, FileShare.Read);
    public string DestinationFolder(string scope, string entryId)
    {
        var handle = Decode(scope, entryId);
        return Resolve(handle with { RelativePath = Path.GetDirectoryName(handle.RelativePath) ?? "" });
    }
    public string EntryForSavedFile(string scope, string sourceEntryId, string filename)
    {
        var handle = Decode(scope, sourceEntryId);
        return Encode(handle with { RelativePath = Path.Combine(Path.GetDirectoryName(handle.RelativePath) ?? "", filename), Length = null, ModifiedTicks = null, CreatedTicks = null });
    }
    public string JournalDirectory(string scope)
    {
        var path = Path.Combine(_options.AssetDirectory, "saves", ScopeKey(scope));
        Directory.CreateDirectory(path);
        return path;
    }
    private string AssetPath(string scope, string id)
    {
        if (id.Length != 129 || !id.StartsWith(ScopeKey(scope) + "-", StringComparison.Ordinal) || id.Where((_, i) => i != 64).Any(c => !char.IsAsciiHexDigit(c)))
        { throw new UnauthorizedAccessException("The image does not belong to this workspace."); }
        return Path.Combine(_options.AssetDirectory, id + ".image");
    }
    private static string ScopeKey(string scope) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
    private string Encode(FileHandle value) => _protector.Protect(JsonSerializer.Serialize(value));
    private FileHandle Decode(string scope, string id)
    {
        try
        {
            var value = JsonSerializer.Deserialize<FileHandle>(_protector.Unprotect(id));
            if (value is null || value.Scope != scope) { throw new UnauthorizedAccessException("This file handle belongs to another workspace."); }
            return value;
        }
        catch (CryptographicException) { throw new UnauthorizedAccessException("The file handle is invalid. Refresh Files."); }
    }
    private string Resolve(FileHandle handle)
    {
        if (!_options.Roots.TryGetValue(handle.Root, out var configured)) { throw new IOException("Downloads is not configured on this IntoChat host."); }
        var relative = handle.RelativePath;
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split(['/', '\\']).Any(s => s is ".." or "."))
        { throw new UnauthorizedAccessException("The path is outside the configured folder."); }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (path != root && !path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new UnauthorizedAccessException("The path is outside the configured folder."); }
        var current = root;
        if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) { throw new UnauthorizedAccessException("Linked roots are not supported."); }
        foreach (var part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) { throw new UnauthorizedAccessException("Linked files and folders are not supported."); }
        }
        return path;
    }
    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
}