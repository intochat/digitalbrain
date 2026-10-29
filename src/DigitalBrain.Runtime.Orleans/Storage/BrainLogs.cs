using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace DigitalBrain.Runtime.Orleans;

// The open brain logs of this silo, one per brain, each claimed through the configured ownership.
internal sealed class BrainLogs : IAsyncDisposable
{
    private readonly BrainStorageKeys keys;
    private readonly Serializer serializer;
    private readonly JournalPayloads payloads;
    private readonly TimeProvider time;
    private readonly IRetentionPolicy retention;
    private readonly Dictionary<string, Task<BrainLog>> logs = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private readonly IBrainLogFiles? files;
    private readonly IBrainLogOwnership ownership;
    private bool disposed;

    public BrainLogs(BrainStorageKeys keys, Serializer serializer, JournalPayloads payloads, TimeProvider time, IRetentionPolicy retention,
        IOptions<BrainStoreOptions> options, IBrainLogOwnership ownership, IBrainLogFiles? files = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.ownership = ownership;
        this.files = files;
        this.keys = keys;
        this.serializer = serializer;
        this.payloads = payloads;
        this.time = time;
        this.retention = retention;
        Directory.CreateDirectory(options.Value.Directory);
    }

    public async Task<BrainLog> For(string brainId)
    {
        var (log, evicted) = Lookup(brainId);
        // The evicted log's lease release can be a network call; it runs after the gate is free so other brains are not held behind it.
        if (evicted is not null) { await evicted.DisposeAsync(); }
        return await log;
    }

    private (Task<BrainLog> Log, BrainLog? Evicted) Lookup(string brainId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BrainLog? evicted = null;
            if (logs.TryGetValue(brainId, out var log))
            {
                // A log that lost its ownership or failed a write is dropped and claimed afresh; its release runs outside the gate.
                if (!(log.IsCompletedSuccessfully && log.Result is { Usable: false } stale)) { return (log, null); }
                logs.Remove(brainId);
                evicted = stale;
            }
            log = Open(brainId);
            if (!log.IsFaulted) { logs[brainId] = log; } // Opens usually complete synchronously; a refused claim is not cached.
            return (log, evicted);
        }
    }

    // Claims the brain's ownership before touching its key, so a silo that does not own the brain creates nothing.
    private async Task<BrainLog> Open(string brainId)
    {
        try
        {
            var directory = keys.DirectoryOf(brainId);
            var lease = await ownership.ClaimAsync(directory, brainId, CancellationToken.None);
            try { return new(directory, brainId, await keys.GetOrCreate(brainId, CancellationToken.None), serializer, payloads, time, retention, lease, files); }
            catch { await lease.DisposeAsync(); throw; }
        }
        catch
        {
            lock (gate) { if (!disposed) { logs.Remove(brainId); } }
            throw;
        }
    }

    internal async Task<bool> CheckStorageAsync(string brainId, CancellationToken ct)
    {
        try { return await (await For(brainId).WaitAsync(ct)).CheckStorage(ct); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        Task<BrainLog>[] opening;
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            opening = [.. logs.Values];
        }
        foreach (var task in opening)
        {
            BrainLog log;
            try { log = await task; }
            catch { continue; } // Failed opens already released their ownership.
            await log.DisposeAsync();
        }
    }
}
