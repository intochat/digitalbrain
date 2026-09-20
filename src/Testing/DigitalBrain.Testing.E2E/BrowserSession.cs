using Microsoft.Playwright;

namespace DigitalBrain.Testing.E2E;

public sealed class BrowserSession : IAsyncDisposable
{
    private readonly IBrowserContext _context;
    private readonly string? _artifacts;
    private readonly CancellationTokenRegistration _cancellation;
    private int _disposed;
    internal BrowserSession(IBrowserContext context, string? artifacts, CancellationToken ct)
    {
        _context = context;
        _artifacts = artifacts;
        _cancellation = ct.Register(() =>
        {
            var closing = context.CloseAsync();
            _ = closing.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        });
    }
    public IPage Page { get; internal set; } = null!;
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        _cancellation.Dispose();
        try
        {
            if (_artifacts is not null && Page is not null && !Page.IsClosed)
            {
                Directory.CreateDirectory(_artifacts);
                var prefix = Path.Combine(_artifacts, "browser-" + Guid.NewGuid().ToString("N"));
                await Page.ScreenshotAsync(new() { Path = prefix + ".png", Timeout = 5_000 }).ConfigureAwait(false);
                await _context.Tracing.StopAsync(new() { Path = prefix + ".zip" }).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        catch { /* Artifact failure must not prevent browser cleanup. */ }
        finally { await _context.CloseAsync().ConfigureAwait(false); }
    }
}
