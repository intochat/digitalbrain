using System.Diagnostics;
using Aspire.Hosting;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Playwright;
using Orleans;

namespace DigitalBrain.Testing.E2E;

public sealed class E2EBrain : IDigitalBrain, ITrackedBrain
{
    private readonly SemaphoreSlim _browserGate = new(1);
    private readonly ResolvedBrowserOptions _options;
    private readonly AspireTestSession _session;
    private readonly TestSessionLifetime _lifetime;
    private IBrowser? _browser;
    private BrowserSession? _primaryBrowser;

    internal E2EBrain(AspireTestSession session, ResolvedBrowserOptions options)
    {
        _session = session;
        _lifetime = session.Lifetime;
        _options = options;
    }

    public HttpClient HttpClient => _session.HttpClient;
    public DistributedApplication Application => _session.App;

    public IPage Page => _primaryBrowser?.Page
        ?? throw new InvalidOperationException("This test exposes no browser endpoint. Configure a module to run its web app.");

    int ITrackedBrain.BufferCapacity => new BrainOptions().BufferCapacity;
    TestExecutionOptions ITrackedBrain.Execution => _session.Options;
    void ITrackedBrain.Track(IAsyncDisposable resource) => _lifetime.Own("observation", resource);

    public T Get<T>(string id) where T : class, IGrainWithStringKey => _session.Brain.Get<T>(id);

    public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        => _session.Brain.SubscribeAsync<T>(source, cancellationToken);

    internal async Task StartBrowserAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_session.BrowserEndpoint is not null)
        { _primaryBrowser = await OpenBrowserAsync(cancellationToken).ConfigureAwait(false); }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<BrowserSession> OpenBrowserAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = _session.BrowserEndpoint ?? throw new InvalidOperationException("This application configuration exposes no browser endpoint.");
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
                _lifetime.Own("playwright", new AsyncAction(() => { playwright.Dispose(); return ValueTask.CompletedTask; }));
                using var cancelLaunch = deadline.Token.Register(playwright.Dispose);
                _browser = await playwright.Chromium.LaunchAsync(new()
                {
                    Headless = _options.Headless,
                    SlowMo = _options.SlowMoMilliseconds,
                    Timeout = Remaining(),
                }).ConfigureAwait(false);
                _lifetime.Own("browser", _browser);
            }
            deadline.Token.ThrowIfCancellationRequested();
            stage = "browser-context";
            var context = await AcquireAsync(_browser.NewContextAsync(), value => value.CloseAsync(), deadline.Token).ConfigureAwait(false);
            session = new BrowserSession(context, _session.Options.ArtifactDirectory, cancellationToken);
            _lifetime.Own("browser-context", session);
            using var cancelStartup = deadline.Token.Register(() => BrowserSession.CloseInBackground(context));
            await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true }).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            session.Page = page;
            page.SetDefaultTimeout((float)_options.AssertionTimeout.TotalMilliseconds);
            stage = "browser-readiness";
            await PreparePageAsync(page, endpoint, _session.BrowserReadySelector, Remaining, deadline.Token).ConfigureAwait(false);
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

    public ValueTask DisposeAsync() => _lifetime.DisposeAsync();

    private sealed class AsyncAction(Func<ValueTask> action) : IAsyncDisposable
    { public ValueTask DisposeAsync() => action(); }
}
