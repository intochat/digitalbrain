using DigitalBrain.Operations;

namespace DigitalBrain.Capabilities.Storage;

/// <summary>One mount point in the virtual file system.</summary>
/// <param name="MountPoint">The top-level root segment (e.g. "drive", "local", "windows").</param>
/// <param name="Label">Display label for the mount.</param>
/// <param name="Storage">The underlying storage.</param>
public sealed record VfsMount(string MountPoint, string Label, IStorage Storage);

/// <summary>A file or folder in the virtual file system.</summary>
/// <param name="Path">The absolute virtual path (e.g. "/drive/docs/report.txt").</param>
/// <param name="Name">Display name of the entry.</param>
/// <param name="Kind">File or Folder.</param>
/// <param name="Size">Size in bytes, if known.</param>
/// <param name="Modified">Last modification timestamp, if known.</param>
/// <param name="Flags">Entry capability flags.</param>
public sealed record VfsItem(
    string Path,
    string Name,
    ItemKind Kind,
    long? Size,
    DateTimeOffset? Modified,
    ItemFlags Flags = ItemFlags.None);

/// <summary>A virtual filesystem aggregating multiple <see cref="IStorage"/> connections under unified mount paths.</summary>
public interface IStorageVfs
{
    /// <summary>Lists every active mount in the virtual file system.</summary>
    Task<IReadOnlyList<VfsMount>> GetMounts(CancellationToken ct = default);

    /// <summary>Retrieves metadata for an item at the specified virtual path, or null if not found.</summary>
    Task<VfsItem?> Stat(string path, CancellationToken ct = default);

    /// <summary>Lists all child items in the given virtual folder path. An empty or "/" path lists the root mounts.</summary>
    Task<IReadOnlyList<VfsItem>> List(string folderPath, CancellationToken ct = default);

    /// <summary>Reads raw content bytes from a virtual file path.</summary>
    Task<Content> Read(string filePath, ByteRange? range = null, CancellationToken ct = default);

    /// <summary>Reads a virtual file decoded as UTF-8 text.</summary>
    Task<string> ReadText(string filePath, CancellationToken ct = default);

    /// <summary>Opens a read stream for a virtual file.</summary>
    Task<StorageReadStream> OpenRead(string filePath, ByteRange? range = null, CancellationToken ct = default);

    /// <summary>Writes or overwrites a file at the specified virtual path.</summary>
    Task<OperationId> Write(string filePath, byte[] content, CancellationToken ct = default);
}

/// <summary>Default implementation of <see cref="IStorageVfs"/> over registered mounts.</summary>
public sealed class StorageVfs : IStorageVfs
{
    private readonly Dictionary<string, VfsMount> _mounts;

    /// <summary>Creates a new virtual filesystem with the specified mounts.</summary>
    public StorageVfs(IEnumerable<VfsMount> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        _mounts = mounts.ToDictionary(
            m => NormalizeMountPoint(m.MountPoint),
            m => m,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<VfsMount>> GetMounts(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<VfsMount>>([.. _mounts.Values]);

    /// <inheritdoc/>
    public async Task<VfsItem?> Stat(string path, CancellationToken ct = default)
    {
        var clean = NormalizePath(path);
        if (clean.Length == 0)
        {
            return new VfsItem("/", "/", ItemKind.Folder, null, null);
        }

        var (mount, relative) = ResolveMount(clean);
        if (relative.Length == 0)
        {
            return new VfsItem($"/{mount.MountPoint}", mount.MountPoint, ItemKind.Folder, null, null);
        }

        var item = await mount.Storage.ResolvePath(relative, ct).ConfigureAwait(false);
        return item is null ? null : ToVfsItem(mount.MountPoint, relative, item);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VfsItem>> List(string folderPath, CancellationToken ct = default)
    {
        var clean = NormalizePath(folderPath);
        if (clean.Length == 0)
        {
            return _mounts.Values
                .Select(m => new VfsItem($"/{m.MountPoint}", m.MountPoint, ItemKind.Folder, null, null))
                .ToArray();
        }

        var (mount, relative) = ResolveMount(clean);
        var folderItem = relative.Length == 0
            ? (await mount.Storage.Roots(ct).ConfigureAwait(false) is { Count: > 0 } roots ? roots[0] : null)
            : await mount.Storage.ResolvePath(relative, ct).ConfigureAwait(false);

        if (folderItem is null)
        {
            throw new System.IO.DirectoryNotFoundException($"Virtual directory not found: '{folderPath}'.");
        }

        var page = await mount.Storage.List(folderItem.Id, new ListQuery(PageSize: 500), ct).ConfigureAwait(false);
        var prefix = relative.Length == 0 ? string.Empty : $"{relative}/";

        return page.Items
            .Select(item => ToVfsItem(mount.MountPoint, $"{prefix}{item.Name}", item))
            .ToArray();
    }

    /// <inheritdoc/>
    public async Task<Content> Read(string filePath, ByteRange? range = null, CancellationToken ct = default)
    {
        var clean = NormalizePath(filePath);
        var (mount, relative) = ResolveMount(clean);
        if (relative.Length == 0)
        {
            throw new System.IO.FileNotFoundException("Cannot read root mount directory as a file.");
        }

        return await mount.Storage.ReadByPath(relative, range, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> ReadText(string filePath, CancellationToken ct = default)
    {
        var clean = NormalizePath(filePath);
        var (mount, relative) = ResolveMount(clean);
        if (relative.Length == 0)
        {
            throw new System.IO.FileNotFoundException("Cannot read root mount directory as a file.");
        }

        return await mount.Storage.ReadTextByPath(relative, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<StorageReadStream> OpenRead(string filePath, ByteRange? range = null, CancellationToken ct = default)
    {
        var clean = NormalizePath(filePath);
        var (mount, relative) = ResolveMount(clean);
        if (relative.Length == 0)
        {
            throw new System.IO.FileNotFoundException("Cannot open root mount directory as a file.");
        }

        var item = await mount.Storage.ResolvePath(relative, ct).ConfigureAwait(false)
            ?? throw new System.IO.FileNotFoundException($"Virtual file not found: '{filePath}'.");

        return await mount.Storage.OpenRead(item.Id, range, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<OperationId> Write(string filePath, byte[] content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var clean = NormalizePath(filePath);
        var (mount, relative) = ResolveMount(clean);
        if (relative.Length == 0)
        {
            throw new System.IO.IOException("Cannot overwrite root mount point.");
        }

        var lastSlash = relative.LastIndexOf('/');
        var folderPath = lastSlash >= 0 ? relative[..lastSlash] : string.Empty;
        var fileName = lastSlash >= 0 ? relative[(lastSlash + 1)..] : relative;

        var parent = folderPath.Length == 0
            ? (await mount.Storage.Roots(ct).ConfigureAwait(false) is { Count: > 0 } mountRoots ? mountRoots[0] : null)
            : await mount.Storage.ResolvePath(folderPath, ct).ConfigureAwait(false);

        if (parent is null)
        {
            throw new System.IO.DirectoryNotFoundException($"Parent directory not found for: '{filePath}'.");
        }

        var plan = WritePlan.WriteFile(parent.Id, fileName, content, ConflictPolicy.Overwrite);
        return await mount.Storage.Write(plan, ct).ConfigureAwait(false);
    }

    private (VfsMount Mount, string RelativePath) ResolveMount(string normalizedPath)
    {
        var slash = normalizedPath.IndexOf('/');
        var mountKey = slash >= 0 ? normalizedPath[..slash] : normalizedPath;
        var relative = slash >= 0 ? normalizedPath[(slash + 1)..] : string.Empty;

        if (!_mounts.TryGetValue(mountKey, out var mount))
        {
            throw new System.IO.DirectoryNotFoundException($"Mount point '{mountKey}' not found in virtual file system.");
        }

        return (mount, relative);
    }

    private static string NormalizeMountPoint(string mountPoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountPoint);
        return mountPoint.Trim('/', '\\').ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) { return string.Empty; }
        return path.Replace('\\', '/').Trim('/');
    }

    private static VfsItem ToVfsItem(string mount, string relative, Item item)
        => new($"/{mount}/{relative}".TrimEnd('/'), item.Name, item.Kind, item.Size, item.Modified, item.Flags);
}
