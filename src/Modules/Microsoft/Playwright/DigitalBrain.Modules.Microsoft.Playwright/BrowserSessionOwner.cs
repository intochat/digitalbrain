namespace DigitalBrain.Microsoft.Playwright;

/// <summary>Owns one transient page connection; replacements invalidate all older work.</summary>
public sealed class BrowserSessionOwner(IBrowserSessionProvider provider) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private Lease? _lease;
    private bool _disposed;

    public async Task Attach(BrowserAttachment attachment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (attachment.Port is < 1 or > 65535 || attachment.SessionId?.Length != 32
            || !attachment.SessionId.All(char.IsAsciiHexDigit))
            { throw new ArgumentException("A loopback port and 32 hex character session marker are required.", nameof(attachment)); }
        ct.ThrowIfCancellationRequested();
        Lease next;
        Lease? previous;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _lease;
            next = new Lease(attachment.SessionId);
            _lease = next;
        }
        previous?.Lifetime.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, next.Lifetime.Token);
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (previous?.Page is { } old) { await old.DisposeAsync().ConfigureAwait(false); }
            linked.Token.ThrowIfCancellationRequested();
            var page = await provider.AttachAsync(attachment, linked.Token).ConfigureAwait(false);
            next.Page = page;
            linked.Token.ThrowIfCancellationRequested();
        }
        catch
        {
            next.Lifetime.Cancel();
            if (next.Page is { } page) { await page.DisposeAsync().ConfigureAwait(false); }
            lock (_sync) { if (ReferenceEquals(_lease, next)) { _lease = null; } }
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task Detach(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Lease? lease;
        lock (_sync)
        {
            lease = _lease;
            if (lease?.Id != sessionId) { return; }
            _lease = null;
        }
        lease.Lifetime.Cancel();
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try { if (lease.Page is { } page) { await page.DisposeAsync().ConfigureAwait(false); } }
        finally { _gate.Release(); }
    }

    public BrowserSession Read()
    {
        lock (_sync)
        {
            var lease = _lease;
            return lease?.Page is { Connected: true } page && !lease.Lifetime.IsCancellationRequested
                ? new(true, lease.Id, page.Url, page.Title)
                : new(false, lease?.Id, null, null);
        }
    }

    public Task<BrowserObservation> Navigate(string url, CancellationToken ct = default)
    {
        PublicBrowserNetwork.PublicUri(url);
        return Run((page, token) => page.NavigateAsync(url, token), ct);
    }
    public Task<BrowserObservation> Snapshot(CancellationToken ct = default) => Run((page, token) => page.SnapshotAsync(token), ct);
    public Task<BrowserObservation> Click(string selector, CancellationToken ct = default) => Run((page, token) => page.ClickAsync(selector, token), ct);
    public Task<BrowserObservation> Fill(string selector, string value, CancellationToken ct = default) => Run((page, token) => page.FillAsync(selector, value, token), ct);

    private async Task<BrowserObservation> Run(Func<IBrowserPageSession, CancellationToken, Task<BrowserObservation>> action, CancellationToken ct)
    {
        Lease lease;
        lock (_sync) { lease = _lease ?? throw new InvalidOperationException("The visible browser is not connected."); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.Lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var page = lease.Page;
            if (page is not { Connected: true }) { throw new InvalidOperationException("The visible browser is disconnected."); }
            var result = await action(page, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        string? id;
        lock (_sync) { _disposed = true; id = _lease?.Id; }
        if (id is not null) { await Detach(id).ConfigureAwait(false); }
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Release();
        GC.SuppressFinalize(this);
    }

    private sealed class Lease(string id)
    {
        public string Id { get; } = id;
        public CancellationTokenSource Lifetime { get; } = new();
        public IBrowserPageSession? Page { get; set; }
    }
}
