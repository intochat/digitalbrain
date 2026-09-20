using DigitalBrain.Testing.Hosting;
using Microsoft.Playwright;

namespace DigitalBrain.Testing.E2E;

public sealed class E2EBrain : HostedBrain
{
    private readonly SemaphoreSlim _browserGate = new(1);
    private IBrowser? _browser;
    internal E2EBrain(AspireTestSession session) : base(session) { }

    public Task<BrowserSession> OpenBrowserAsync(CancellationToken cancellationToken = default)
        => OpenBrowserAsync(BrowserOptions.Default, cancellationToken);

    public async Task<BrowserSession> OpenBrowserAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        var endpoint = Session.BrowserEndpoint ?? throw new InvalidOperationException("This application configuration exposes no browser endpoint.");
        await _browserGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_browser is null)
            {
                var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                Lifetime.Own("playwright", new AsyncAction(() => { playwright.Dispose(); return ValueTask.CompletedTask; }));
                _browser = await playwright.Chromium.LaunchAsync(new()
                {
                    Headless = options.Headless,
                    SlowMo = options.SlowMoMilliseconds,
                    Timeout = 30_000,
                }).ConfigureAwait(false);
                Lifetime.Own("browser", _browser);
            }
            var context = await _browser.NewContextAsync().ConfigureAwait(false);
            var session = new BrowserSession(context, Session.Options.ArtifactDirectory, cancellationToken);
            Lifetime.Own("browser-context", session);
            try
            {
                await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true }).ConfigureAwait(false);
                var page = await context.NewPageAsync().ConfigureAwait(false);
                session.Page = page;
                page.SetDefaultTimeout(60_000);
                await page.GotoAsync(endpoint.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 }).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return session;
            }
            catch { await session.DisposeAsync().ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested(); throw; }
        }
        finally { _browserGate.Release(); }
    }
    private sealed class AsyncAction(Func<ValueTask> action) : IAsyncDisposable
    { public ValueTask DisposeAsync() => action(); }
}

public sealed record BrowserOptions
{
    public bool Headless { get; init; } = true;
    public float SlowMoMilliseconds { get; init; }

    public static bool HeadedRequested =>
        string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_HEADED"), "1", StringComparison.OrdinalIgnoreCase);

    public static BrowserOptions Default => new()
    {
        Headless = !HeadedRequested,
        SlowMoMilliseconds = HeadedRequested ? 250 : 0,
    };
}
