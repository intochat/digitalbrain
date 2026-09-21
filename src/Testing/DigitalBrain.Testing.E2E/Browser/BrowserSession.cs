using Microsoft.Playwright;

namespace DigitalBrain.Testing.E2E;

public sealed class BrowserSession : IAsyncDisposable
{
    private readonly IBrowserContext _context;
    private readonly string? _artifacts;
    private readonly CancellationTokenRegistration _cancellation;
    private readonly Queue<string> _diagnostics = new();
    private readonly object _gate = new();
    private int _disposed;
    private IPage _page = null!;

    internal BrowserSession(IBrowserContext context, string? artifacts, CancellationToken ct)
    {
        _context = context;
        _artifacts = artifacts;
        _cancellation = ct.Register(() => CloseInBackground(context));
    }

    public IPage Page
    {
        get => _page;
        internal set
        {
            _page = value;
            // Omit message bodies and URL query strings: they can contain credentials or user data.
            value.Console += (_, message) => Record("console:" + message.Type);
            value.PageError += (_, _) => Record("page-error");
            value.RequestFailed += (_, request) => Record("request-failed:" + request.Method);
        }
    }

    internal static void CloseInBackground(IBrowserContext context)
    {
        var closing = context.CloseAsync();
        _ = closing.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
    }

    private void Record(string message)
    {
        lock (_gate)
        {
            if (_diagnostics.Count == 100) { _diagnostics.Dequeue(); }
            _diagnostics.Enqueue(message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        _cancellation.Dispose();
        try
        {
            if (_artifacts is not null)
            {
                var prefix = Path.Combine(_artifacts, "browser-" + Guid.NewGuid().ToString("N"));
                await Capture(async () =>
                {
                    Directory.CreateDirectory(_artifacts);
                    if (_page is not null && !_page.IsClosed)
                    { await _page.ScreenshotAsync(new() { Path = prefix + ".png", Timeout = 5_000 }).ConfigureAwait(false); }
                }).ConfigureAwait(false);
                await Capture(async () =>
                {
                    Directory.CreateDirectory(_artifacts);
                    await _context.Tracing.StopAsync(new() { Path = prefix + ".zip" }).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }).ConfigureAwait(false);
                await Capture(() =>
                {
                    string[] diagnostics;
                    lock (_gate) { diagnostics = _diagnostics.ToArray(); }
                    return File.WriteAllLinesAsync(prefix + ".log", diagnostics);
                }).ConfigureAwait(false);
            }
        }
        finally { await _context.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
    }

    private static async Task Capture(Func<Task> capture)
    {
        try { await capture().ConfigureAwait(false); }
        catch { /* Each artifact is best effort; always attempt the remaining artifacts and cleanup. */ }
    }
}
