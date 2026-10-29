namespace DigitalBrain.Capabilities.Storage;

/// <summary>Keeps write streams on their storage host, bound to a stamped caller, with bounded chunks and idle expiry.</summary>
public sealed class StorageUploadSessions : IStorageUpload, IAsyncDisposable
{
    /// <summary>The largest accepted chunk.</summary>
    public const int ChunkSize = 256 * 1024;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly Func<WritePlan, CancellationToken, Task<StorageWriteStream>> _openWrite;
    private readonly Func<string> _caller;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _sweeper;

    /// <summary>The caller function must return runtime-authenticated identity, never an argument supplied by an app.</summary>
    public StorageUploadSessions(Func<IStorage> storage, Func<string> caller) : this(storage, caller, null) { }

    /// <summary>Creates sessions with a supplied clock.</summary>
    public StorageUploadSessions(Func<IStorage> storage, Func<string> caller, TimeProvider? time)
        : this((plan, ct) => storage().OpenWrite(plan, ct), caller, time)
    {
        ArgumentNullException.ThrowIfNull(storage);
    }

    /// <summary>Creates sessions over a storage's already-authorized primitive stream opener.</summary>
    public StorageUploadSessions(Func<WritePlan, CancellationToken, Task<StorageWriteStream>> openWrite, Func<string> caller, TimeProvider? time)
    {
        _openWrite = openWrite ?? throw new ArgumentNullException(nameof(openWrite));
        _caller = caller ?? throw new ArgumentNullException(nameof(caller));
        _time = time ?? TimeProvider.System;
        _sweeper = Sweep();
    }

    /// <inheritdoc />
    public async Task<string> BeginUpload(WritePlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Kind != WriteKind.WriteFile || plan.DryRun || plan.Inline is not null) { throw new Unsupported(plan.TargetFolder); }
        var caller = Caller();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_stopping.IsCancellationRequested, this);
            await Expire().ConfigureAwait(false);
            if (_sessions.Count >= 32 || _sessions.Values.Count(s => s.Owner == caller) >= 8) { throw new Unavailable(plan.TargetFolder); }
            var write = await _openWrite(plan, ct).ConfigureAwait(false);
            var id = Guid.NewGuid().ToString("N");
            _sessions.Add(id, new Session(caller, write, _time.GetUtcNow() + Lifetime));
            return id;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task AppendUpload(string upload, long offset, byte[] bytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is 0 or > ChunkSize) { throw new ArgumentOutOfRangeException(nameof(bytes)); }
        var caller = Caller();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Expire().ConfigureAwait(false);
            var session = Get(upload, caller);
            if (offset != session.Length) { throw new Conflict(null); }
            try
            {
                await session.Write.Content.WriteAsync(bytes, ct).ConfigureAwait(false);
                session.Length += bytes.Length;
                session.Expires = _time.GetUtcNow() + Lifetime;
            }
            catch
            {
                _sessions.Remove(upload);
                await session.Write.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<ItemId> CommitUpload(string upload, long length, CancellationToken ct = default)
    {
        var caller = Caller();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Expire().ConfigureAwait(false);
            var session = Get(upload, caller);
            if (length != session.Length) { throw new Conflict(null); }
            _sessions.Remove(upload);
            await using (session.Write.ConfigureAwait(false))
            {
                return await session.Write.Commit(ct).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task AbortUpload(string upload, CancellationToken ct = default)
    {
        var caller = Caller();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(upload, out var session)) { return; }
            if (session.Owner != caller) { throw new Denied(null); }
            _sessions.Remove(upload);
            await session.Write.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private string Caller()
    {
        var caller = _caller();
        if (string.IsNullOrWhiteSpace(caller)) { throw new Denied(null); }
        return caller;
    }

    private Session Get(string upload, string caller)
    {
        if (!_sessions.TryGetValue(upload, out var session)) { throw new NotFound(null); }
        if (session.Owner != caller) { throw new Denied(null); }
        return session;
    }

    private async Task Expire()
    {
        foreach (var (id, session) in _sessions.Where(pair => pair.Value.Expires <= _time.GetUtcNow()).ToArray())
        {
            _sessions.Remove(id);
            await session.Write.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task Sweep()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(_stopping.Token).ConfigureAwait(false))
            {
                await _gate.WaitAsync(_stopping.Token).ConfigureAwait(false);
                try { await Expire().ConfigureAwait(false); }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        try { await _sweeper.ConfigureAwait(false); }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var session in _sessions.Values) { await session.Write.DisposeAsync().ConfigureAwait(false); }
                _sessions.Clear();
            }
            finally { _gate.Release(); }
            _gate.Dispose();
            _stopping.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private sealed class Session(string owner, StorageWriteStream write, DateTimeOffset expires)
    {
        public string Owner { get; } = owner;
        public StorageWriteStream Write { get; } = write;
        public long Length { get; set; }
        public DateTimeOffset Expires { get; set; } = expires;
    }
}
