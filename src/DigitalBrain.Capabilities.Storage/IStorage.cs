using System.ComponentModel;
using System.Text;
using DigitalBrain.Contracts;
using DigitalBrain.Operations;

namespace DigitalBrain.Capabilities.Storage;

/// <summary>
/// The storage capability, version 1: files and folders behind one storage connection (a local disk, a cloud drive,
/// a shared folder). Items are addressed by an opaque <see cref="ItemId"/> that only the module providing the
/// connection can interpret. Failures surface as <see cref="StorageException"/> subtypes.
/// </summary>
[Capability("storage", 1), NeuronAlias("storage")]
public interface IStorage
{
    /// <summary>Describes this storage connection: its label, kind, quota and what it allows.</summary>
    [Description("Describe this storage: its label, kind, quota and the actions it allows.")]
    Task<StorageDescriptor> Describe(CancellationToken ct = default);

    /// <summary>The top-level folders this storage offers.</summary>
    [Description("The top-level folders this storage offers.")]
    Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default);

    /// <summary>One page of the items inside a folder, sorted per <paramref name="query"/>.</summary>
    [Description("List one page of the files and folders inside a folder, sorted.")]
    Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default);

    /// <summary>The current facts about one item.</summary>
    [Description("Get the name, kind, size, modified time and allowed actions of one item.")]
    Task<Item> Stat(ItemId id, CancellationToken ct = default);

    /// <summary>Reads the bytes of a file, whole or the given range.</summary>
    [Description("Read the bytes of a file, whole or a byte range.")]
    Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default);

    /// <summary>Starts a write operation (create folder, write file, move, copy, rename or delete) and returns its id; progress arrives as operation signals.</summary>
    [Description("Start a change: create a folder, write a file, move, copy, rename or delete. Returns the operation id.")]
    Task<OperationId> Write(WritePlan plan, CancellationToken ct = default);

    /// <summary>One page of the items matching a search.</summary>
    [Description("Search this storage for items by name.")]
    Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default);

    /// <summary>The folders containing an item, root first, excluding the item itself.</summary>
    [Description("The folders that contain an item, from the root down.")]
    Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default);

    /// <summary>
    /// Opens a file for streaming read, whole or the given range. The handle is host-local: it is never serialised
    /// across a grain boundary.
    /// </summary>
    [Description("Open a file for streaming read, whole or a byte range. Host-local only.")]
    Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default);

    /// <summary>
    /// Opens a streaming write for a <see cref="WriteKind.WriteFile"/> plan. With <see cref="ConflictPolicy.Fail"/> an
    /// existing name throws <see cref="Conflict"/> before any byte is accepted; a connection whose scope forbids
    /// writing throws <see cref="Unsupported"/>. The handle is host-local.
    /// </summary>
    [Description("Open a streaming write for a file; commit to finish, abort or dispose to discard. Host-local only.")]
    Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default);
}

/// <summary>Helpers over <see cref="IStorage"/>.</summary>
public static class StorageExtensions
{
    /// <summary>Reads a whole file and decodes it as UTF-8 text.</summary>
    public static async Task<string> ReadText(this IStorage storage, ItemId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var content = await storage.Read(id, null, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(content.Bytes.Span);
    }

    /// <summary>Resolves an item by traversing path segments relative to storage roots.</summary>
    public static async Task<Item?> ResolvePath(this IStorage storage, string relativePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(relativePath);

        var segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var roots = await storage.Roots(ct).ConfigureAwait(false);
        if (segments.Length == 0)
        {
            return roots.Count > 0 ? roots[0] : null;
        }

        Item? current = roots.FirstOrDefault(r => string.Equals(r.Name, segments[0], StringComparison.OrdinalIgnoreCase));
        var startIndex = 1;
        if (current is null && roots.Count == 1)
        {
            current = roots[0];
            startIndex = 0;
        }

        if (current is null)
        {
            return null;
        }

        for (var i = startIndex; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (current.Kind != ItemKind.Folder)
            {
                return null;
            }

            var page = await storage.List(current.Id, new ListQuery(PageSize: 100), ct).ConfigureAwait(false);
            var next = page.Items.FirstOrDefault(item => string.Equals(item.Name, segment, StringComparison.OrdinalIgnoreCase));
            var cursor = page.NextCursor;
            while (next is null && !string.IsNullOrEmpty(cursor))
            {
                page = await storage.List(current.Id, new ListQuery(Cursor: cursor, PageSize: 100), ct).ConfigureAwait(false);
                next = page.Items.FirstOrDefault(item => string.Equals(item.Name, segment, StringComparison.OrdinalIgnoreCase));
                cursor = page.NextCursor;
            }

            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    /// <summary>Reads a file's content resolved by its relative path.</summary>
    public static async Task<Content> ReadByPath(this IStorage storage, string relativePath, ByteRange? range = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var item = await storage.ResolvePath(relativePath, ct).ConfigureAwait(false)
            ?? throw new System.IO.FileNotFoundException($"File '{relativePath}' was not found in storage.");
        return await storage.Read(item.Id, range, ct).ConfigureAwait(false);
    }

    /// <summary>Reads a whole file and decodes it as UTF-8 text resolved by its relative path.</summary>
    public static async Task<string> ReadTextByPath(this IStorage storage, string relativePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var item = await storage.ResolvePath(relativePath, ct).ConfigureAwait(false)
            ?? throw new System.IO.FileNotFoundException($"File '{relativePath}' was not found in storage.");
        return await storage.ReadText(item.Id, ct).ConfigureAwait(false);
    }
}
