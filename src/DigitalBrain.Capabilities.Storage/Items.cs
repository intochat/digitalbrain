namespace DigitalBrain.Capabilities.Storage;

/// <summary>
/// The opaque id of a file or folder. Only the module providing the storage connection interprets it; callers never
/// parse it as a path. Equality is ordinal on <see cref="Value"/>.
/// </summary>
/// <param name="Value">The opaque id.</param>
public readonly record struct ItemId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>What an item is.</summary>
public enum ItemKind
{
    /// <summary>A file with content.</summary>
    File,

    /// <summary>A folder holding other items.</summary>
    Folder,

    /// <summary>A link (shortcut) to another item; see <see cref="Item.Target"/>.</summary>
    Link,
}

/// <summary>What a caller may do with an item (or, on <see cref="StorageDescriptor"/>, with the storage).</summary>
[Flags]
public enum ItemFlags
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The content can be read.</summary>
    Read = 1,

    /// <summary>The content can be written.</summary>
    Write = 2,

    /// <summary>The item can be moved to the trash.</summary>
    Trash = 4,

    /// <summary>The item can be deleted permanently.</summary>
    Delete = 8,

    /// <summary>The item can be moved.</summary>
    Move = 16,

    /// <summary>The item can be copied.</summary>
    Copy = 32,

    /// <summary>The item can be renamed.</summary>
    Rename = 64,

    /// <summary>The folder (or storage) can be searched.</summary>
    Search = 128,

    /// <summary>Folders can be created inside.</summary>
    CreateFolder = 256,

    /// <summary>The item is shared with others.</summary>
    Shared = 512,

    /// <summary>The content lives only in the cloud; reading downloads it.</summary>
    CloudOnly = 1024,
}

/// <summary>A file, folder or link.</summary>
/// <param name="Id">The opaque id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Kind">File, folder or link.</param>
/// <param name="Size">The size in bytes, when known.</param>
/// <param name="Modified">The last modification time, when known.</param>
/// <param name="Flags">What the caller may do with it.</param>
/// <param name="Hash">A content hash in <c>algorithm:value</c> form (for example <c>md5:...</c>), when the storage offers one.</param>
/// <param name="Target">For a link, the item it points to.</param>
public sealed record Item(
    ItemId Id,
    string Name,
    ItemKind Kind,
    long? Size,
    DateTimeOffset? Modified,
    ItemFlags Flags,
    string? Hash = null,
    ItemId? Target = null);

/// <summary>What a storage connection is.</summary>
/// <param name="Label">The display label (for example "Work Drive").</param>
/// <param name="Kind">The kind of storage (for example <c>local</c>, <c>google-drive</c>).</param>
/// <param name="Quota">The space used and available.</param>
/// <param name="Flags">The actions the storage supports at all.</param>
public sealed record StorageDescriptor(string Label, string Kind, Quota Quota, ItemFlags Flags);

/// <summary>A byte range of a file.</summary>
public readonly record struct ByteRange
{
    /// <summary>Creates a range.</summary>
    /// <param name="Offset">The first byte, zero-based.</param>
    /// <param name="Length">The number of bytes, or null for "to the end".</param>
    [System.Text.Json.Serialization.JsonConstructor]
    public ByteRange(long Offset, long? Length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Offset);
        if (Length is { } length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length, nameof(Length));
        }
        this.Offset = Offset;
        this.Length = Length;
    }

    /// <summary>The first byte, zero-based.</summary>
    public long Offset { get; }

    /// <summary>The number of bytes, or null for "to the end".</summary>
    public long? Length { get; }
}

/// <summary>Bytes read from a file.</summary>
/// <param name="Bytes">The bytes (the requested range, or the whole file).</param>
/// <param name="MediaType">The media type, for example <c>text/plain</c>.</param>
/// <param name="TotalLength">The whole file length, when known.</param>
public sealed record Content(ReadOnlyMemory<byte> Bytes, string MediaType, long? TotalLength);

/// <summary>One page of results.</summary>
/// <param name="Items">The items on this page.</param>
/// <param name="NextCursor">The cursor for the next page, or null on the last page.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);
