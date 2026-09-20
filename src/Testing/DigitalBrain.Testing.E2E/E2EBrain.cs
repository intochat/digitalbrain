using System.Diagnostics;
using DigitalBrain.Testing.Hosting;
using Microsoft.Playwright;

namespace DigitalBrain.Testing.E2E;

public sealed class E2EBrain : HostedBrain
{
    private readonly SemaphoreSlim _browserGate = new(1);
    private readonly ResolvedBrowserOptions _options;
    private IBrowser? _browser;

    internal E2EBrain(AspireTestSession session, ResolvedBrowserOptions options) : base(session) => _options = options;

    public async Task<BrowserSession> OpenBrowserAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = Session.BrowserEndpoint ?? throw new InvalidOperationException("This application configuration exposes no browser endpoint.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.StartupTimeout);
        var watch = Stopwatch.StartNew();
        var stage = "browser-lock";
        var acquired = false;
        BrowserSession? session = null;
        try
        {
            await _browserGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            acquired = true;
            if (_browser is null)
            {
                stage = "browser-launch";
                var playwright = await AcquireAsync(Playwright.CreateAsync(),
                    value => { value.Dispose(); return Task.CompletedTask; }, deadline.Token).ConfigureAwait(false);
                Lifetime.Own("playwright", new AsyncAction(() => { playwright.Dispose(); return ValueTask.CompletedTask; }));
                using var cancelLaunch = deadline.Token.Register(playwright.Dispose);
                _browser = await playwright.Chromium.LaunchAsync(new()
                {
                    Headless = _options.Headless,
                    SlowMo = _options.SlowMoMilliseconds,
                    Timeout = Remaining(),
                }).ConfigureAwait(false);
                Lifetime.Own("browser", _browser);
            }
            deadline.Token.ThrowIfCancellationRequested();
            stage = "browser-context";
            var context = await AcquireAsync(_browser.NewContextAsync(), value => value.CloseAsync(), deadline.Token).ConfigureAwait(false);
            session = new BrowserSession(context, Session.Options.ArtifactDirectory, cancellationToken);
            Lifetime.Own("browser-context", session);
            using var cancelStartup = deadline.Token.Register(() => BrowserSession.CloseInBackground(context));
            await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true }).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            session.Page = page;
            page.SetDefaultTimeout((float)_options.AssertionTimeout.TotalMilliseconds);
            stage = "browser-readiness";
            await PreparePageAsync(page, endpoint, Session.BrowserReadySelector, Remaining, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return session;
        }
        catch (Exception error)
        {
            if (session is not null)
            {
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { error.Data["BrowserCleanupFailure"] = cleanup; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested)
                { throw new TimeoutException($"Browser startup timed out at '{stage}' after {_options.StartupTimeout}.", error); }
            throw new InvalidOperationException($"Browser startup failed at '{stage}'.", error);
        }
        finally { if (acquired) { _browserGate.Release(); } }

        float Remaining()
        {
            deadline.Token.ThrowIfCancellationRequested();
            return (float)Math.Max(1, (_options.StartupTimeout - watch.Elapsed).TotalMilliseconds);
        }
    }

    internal static async Task PreparePageAsync(IPage page, Uri endpoint, string? readySelector,
        Func<float> remaining, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await page.GotoAsync(endpoint.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = remaining() }).ConfigureAwait(false);
            if (readySelector is null) { return; }
            try
            {
                await page.Locator(readySelector).First.WaitForAsync(new()
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = Math.Min(8_000, remaining()),
                }).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException) when (!ct.IsCancellationRequested && remaining() > 2_000)
            {
                // A healthy development server can answer before its first build is available.
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
        }
    }

    internal static async Task<T> AcquireAsync<T>(Task<T> acquisition, Func<T, Task> release, CancellationToken ct)
    {
        try { return await acquisition.WaitAsync(ct).ConfigureAwait(false); }
        catch
        {
            // A browser protocol operation may finish after cancellation; do not leak its result.
            _ = acquisition.ContinueWith(async completed =>
            {
                if (completed.IsCompletedSuccessfully)
                {
                    try { await release(completed.Result).ConfigureAwait(false); }
                    catch { /* Best effort after ownership could not be acquired. */ }
                }
                else { _ = completed.Exception; }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
            throw;
        }
    }

    private sealed class AsyncAction(Func<ValueTask> action) : IAsyncDisposable
    { public ValueTask DisposeAsync() => action(); }
}
