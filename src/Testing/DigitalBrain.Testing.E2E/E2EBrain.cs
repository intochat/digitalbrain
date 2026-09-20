using DigitalBrain.Testing.Hosting;
using Microsoft.Playwright;

using BrowserOptions = DigitalBrain.Testing.BrowserOptions;

namespace DigitalBrain.Testing.E2E;

public sealed class E2EBrain : HostedBrain
{
    private readonly SemaphoreSlim _browserGate = new(1);
    private IBrowser? _browser;
    internal E2EBrain(AspireTestSession session) : base(session) { }

    public Task<BrowserSession> OpenBrowserAsync(CancellationToken cancellationToken = default)
        => OpenBrowserAsync(BrowserOptions.Resolve(Session.Options.Browser.Headless), cancellationToken);

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
                var launch = BrowserOptions.Resolve(options.Headless);
                _browser = await playwright.Chromium.LaunchAsync(new()
                {
                    Headless = launch.Headless,
                    SlowMo = launch.SlowMoMilliseconds,
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
                var uri = endpoint.AbsoluteUri.Contains('?', StringComparison.Ordinal)
                    ? endpoint.AbsoluteUri + "&semantics=true"
                    : endpoint.AbsoluteUri + "?semantics=true";
                await page.GotoAsync(uri, new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 }).ConfigureAwait(false);
                await page.Locator("flt-glass-pane, flutter-view").First
                    .WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 60_000 }).ConfigureAwait(false);
                var placeholder = page.Locator("flt-semantics-placeholder");
                if (await placeholder.CountAsync().ConfigureAwait(false) > 0)
                {
                    await placeholder.First.EvaluateAsync("element => element.click()").ConfigureAwait(false);
                }

                await page.Locator("flt-semantics").First
                    .WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 30_000 }).ConfigureAwait(false);
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
