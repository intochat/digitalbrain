namespace DigitalBrain.Capabilities.Storage;

/// <summary>A bounded transport for host-local write streams. The storage keeps the stream; callers send ordered chunks.</summary>
public interface IStorageUpload
{
    /// <summary>Checks the plan and opens an expiring upload owned by the authenticated caller.</summary>
    Task<string> BeginUpload(WritePlan plan, CancellationToken ct = default);

    /// <summary>Appends at the exact current offset. Chunks are at most 256 KiB; a mismatched offset is refused.</summary>
    Task AppendUpload(string upload, long offset, byte[] bytes, CancellationToken ct = default);

    /// <summary>Commits exactly the supplied total length and returns the storage's item id.</summary>
    Task<ItemId> CommitUpload(string upload, long length, CancellationToken ct = default);

    /// <summary>Discards an upload. Unknown or already finished uploads are harmless.</summary>
    Task AbortUpload(string upload, CancellationToken ct = default);
}
