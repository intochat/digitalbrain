namespace DigitalBrain.Capabilities.Storage;

/// <summary>
/// A streaming read from <see cref="IStorage.OpenRead"/>. Host-local: never serialised across a grain boundary.
/// Disposing it disposes <see cref="Content"/>.
/// </summary>
/// <param name="content">The content stream (the requested range, or the whole file).</param>
/// <param name="length">The length of <paramref name="content"/>, when known.</param>
/// <param name="contentType">The media type.</param>
/// <param name="eTag">A version tag for caching, when the storage offers one.</param>
/// <param name="canSeek">True when the storage serves ranges natively.</param>
/// <param name="modified">The last modification time, when known.</param>
public sealed class StorageReadStream(Stream content, long? length, string contentType, string? eTag, bool canSeek, DateTimeOffset? modified) : IAsyncDisposable
{
    /// <summary>The content stream.</summary>
    public Stream Content { get; } = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>The length of <see cref="Content"/>, when known.</summary>
    public long? Length { get; } = length;

    /// <summary>The media type.</summary>
    public string ContentType { get; } = contentType;

    /// <summary>A version tag for caching, when known.</summary>
    public string? ETag { get; } = eTag;

    /// <summary>True when the storage serves ranges natively.</summary>
    public bool CanSeek { get; } = canSeek;

    /// <summary>The last modification time, when known.</summary>
    public DateTimeOffset? Modified { get; } = modified;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// A streaming write from <see cref="IStorage.OpenWrite"/>. Write to <see cref="Content"/>, then <see cref="Commit"/>.
/// Disposing without committing aborts, so no partial item becomes visible. Host-local: never serialised across a
/// grain boundary. Modules implement <see cref="CommitCore"/> and <see cref="AbortCore"/>.
/// </summary>
public abstract class StorageWriteStream : IAsyncDisposable
{
    private int _state; // 0 open, 1 committed, 2 aborted

    /// <summary>Creates a write handle over the stream the caller writes to.</summary>
    protected StorageWriteStream(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    /// <summary>The stream the caller writes the bytes to.</summary>
    public Stream Content { get; }

    /// <summary>Finishes the write and returns the id of the new or replaced item.</summary>
    public async Task<ItemId> Commit(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidOperationException("The write is already committed or aborted.");
        }
        try { return await CommitCore(ct).ConfigureAwait(false); }
        catch (Exception error)
        {
            Interlocked.Exchange(ref _state, 2);
            try { await AbortCore(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
            { error.Data["StorageAbortFailure"] = cleanupError; }
            throw;
        }
    }

    /// <summary>Discards the write. Does nothing when already committed or aborted.</summary>
    public async Task Abort(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
        {
            await AbortCore(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Makes the written bytes visible as an item.</summary>
    protected abstract Task<ItemId> CommitCore(CancellationToken ct);

    /// <summary>Discards the written bytes.</summary>
    protected abstract Task AbortCore(CancellationToken ct);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try { await Abort(CancellationToken.None).ConfigureAwait(false); }
        finally
        {
            await Content.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
