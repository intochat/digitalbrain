using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.WebBrowser;
using DigitalBrain.Flutter.WebBrowser.Signals;
using DigitalBrain.Microsoft.Playwright;
using DigitalBrain.Testing.Module;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.WebBrowser;

public sealed class WebBrowserFacts
{
    private const string Session = "0123456789abcdef0123456789abcdef";
    private static Task<ModuleBrain> Start(Provider provider) => ModuleTest.Create().WithModule<FlutterModule>()
        .RequireModules([typeof(PlaywrightModule)])
        .ConfigureSilo(silo => silo.Services.AddSingleton<IBrowserSessionProvider>(provider))
        .StartAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Connecting_the_shell_attaches_the_declared_driver_and_publishes_ready_without_a_port()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IWebBrowser>("research/browser");
        await browser.Configure("research/driver", "research/status");
        var connector = brain.Get<IWebBrowserConnector>("research/browser");
        var handler = brain.Get<IPrimitiveTestHandler>("browser-handler");
        await brain.Get<IUiBinding>("research/browser").Bind(handler);
        await using var connected = await brain.Observe<BrowserConnected>(browser, ct);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => connector.Connect(80, Session));
        await Assert.ThrowsAsync<ArgumentException>(() => connector.Connect(12345, "invalid"));
        await connector.Connect(12345, Session);
        Assert.Equal(new BrowserAttachment(12345, Session), Assert.Single(provider.Attachments));
        Assert.True((await brain.Get<IPlaywright>("research/driver").Read()).Connected);
        Assert.False((await brain.Get<IPlaywright>("research/browser").Read()).Connected);
        Assert.Equal("Ready", (await brain.Get<IText>("research/status").Read()).Markdown);
        var signal = await connected.NextAsync(ct: ct);
        Assert.Equal("research/browser", signal.Name);
        Assert.Equal(Session, signal.SessionId);
        Assert.False(string.IsNullOrEmpty(signal.Publisher));
        Assert.Equal(new BrowserConnected("research/browser", Session), Assert.Single(await handler.Events()));
    }

    [Fact]
    public async Task Disconnecting_detaches_the_driver_and_publishes_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IWebBrowser>("research/browser");
        var connector = brain.Get<IWebBrowserConnector>("research/browser");
        await using var disconnected = await brain.Observe<BrowserDisconnected>(browser, ct);
        await connector.Connect(12345, Session);
        await connector.Disconnect(Session);
        Assert.False((await brain.Get<IPlaywright>("research/browser").Read()).Connected);
        Assert.True(Assert.Single(provider.Pages).Disposed);
        Assert.Equal(Session, (await disconnected.NextAsync(ct: ct)).SessionId);
        await connector.Disconnect(Session);
    }

    [Fact]
    public async Task Failed_detachment_does_not_announce_disconnection_and_can_be_retried()
    {
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IWebBrowser>("research/browser");
        await browser.Configure("research/driver", "research/status");
        var connector = brain.Get<IWebBrowserConnector>("research/browser");
        var handler = brain.Get<IPrimitiveTestHandler>("handler");
        await brain.Get<IUiBinding>("research/browser").Bind(handler);
        await connector.Connect(12345, Session);
        var page = Assert.Single(provider.Pages);
        page.FailDispose = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.Disconnect(Session));

        Assert.Equal("Ready", (await brain.Get<IText>("research/status").Read()).Markdown);
        Assert.IsType<BrowserConnected>(Assert.Single(await handler.Events()));
        page.FailDispose = false;
        await connector.Disconnect(Session);
        Assert.Equal("Browser disconnected", (await brain.Get<IText>("research/status").Read()).Markdown);
        Assert.Single((await handler.Events()).OfType<BrowserDisconnected>());
    }

    [Fact]
    public async Task A_pending_detach_cannot_announce_disconnection_or_overwrite_a_replacement_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IWebBrowser>("research/browser");
        await browser.Configure("research/driver", "research/status");
        var connector = brain.Get<IWebBrowserConnector>("research/browser");
        var handler = brain.Get<IPrimitiveTestHandler>("handler");
        await brain.Get<IUiBinding>("research/browser").Bind(handler);
        await connector.Connect(12345, Session);
        var page = Assert.Single(provider.Pages);
        page.PendingDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnect = connector.Disconnect(Session);
        await page.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        try
        {
            Assert.Equal("Ready", (await brain.Get<IText>("research/status").Read()).Markdown);
            Assert.IsType<BrowserConnected>(Assert.Single(await handler.Events()));
            var repeatedDisconnect = connector.Disconnect(Session);
            await browser.Read();
            Assert.False(repeatedDisconnect.IsCompleted);
            var replacement = connector.Connect(12345, new string('b', 32));
            // Read is a scheduling barrier on the reentrant browser grain.
            await browser.Read();
            page.PendingDispose.SetResult();
            await Task.WhenAll(disconnect, repeatedDisconnect, replacement).WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Equal("Ready", (await brain.Get<IText>("research/status").Read()).Markdown);
            Assert.DoesNotContain(await handler.Events(), signal => signal is BrowserDisconnected);
            Assert.True((await brain.Get<IPlaywright>("research/driver").Read()).Ready);
        }
        finally { page.PendingDispose.TrySetResult(); }
    }

    [Fact]
    public async Task Deactivation_detaches_without_changing_status_or_dispatching_ui_events()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IWebBrowser>("research/browser");
        await browser.Configure("research/driver", "research/status");
        var handler = brain.Get<IPrimitiveTestHandler>("handler");
        await brain.Get<IUiBinding>("research/browser").Bind(handler);
        await brain.Get<IWebBrowserConnector>("research/browser").Connect(12345, Session);
        await brain.Get<IText>("research/status").Set("Researching…");

        await brain.DeactivateAsync(browser, ct);

        Assert.True(Assert.Single(provider.Pages).Disposed);
        Assert.Equal("Researching…", (await brain.Get<IText>("research/status").Read()).Markdown);
        Assert.IsType<BrowserConnected>(Assert.Single(await handler.Events()));
    }

    [Fact]
    public async Task Failed_cleanup_during_deactivation_does_not_dispatch_a_disconnect()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IWebBrowser>("research/browser");
        await browser.Configure("research/driver", "research/status");
        var handler = brain.Get<IPrimitiveTestHandler>("handler");
        await brain.Get<IUiBinding>("research/browser").Bind(handler);
        await brain.Get<IWebBrowserConnector>("research/browser").Connect(12345, Session);
        Assert.Single(provider.Pages).FailDispose = true;

        await brain.DeactivateAsync(browser, ct);

        Assert.Equal("Ready", (await brain.Get<IText>("research/status").Read()).Markdown);
        Assert.IsType<BrowserConnected>(Assert.Single(await handler.Events()));
        await browser.Read();
    }

    [Fact]
    public async Task Recomposition_and_rebinding_do_not_replace_a_live_session_or_reset_status()
    {
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IWebBrowser>("research/browser");
        var connector = brain.Get<IWebBrowserConnector>("research/browser");
        var handler = brain.Get<IPrimitiveTestHandler>("handler");
        await browser.Configure("research/driver", "research/status");
        await brain.Get<IUiBinding>("research/browser").Bind(handler);
        await connector.Connect(12345, Session);
        await brain.Get<IText>("research/status").Set("Researching…");
        await browser.Configure("research/driver", "research/status");
        await brain.Get<IUiBinding>("research/browser").Bind(handler);
        await connector.Connect(12345, Session);
        Assert.Single(provider.Attachments);
        Assert.Single(await handler.Events());
        Assert.Equal("Researching…", (await brain.Get<IText>("research/status").Read()).Markdown);
        await Assert.ThrowsAsync<InvalidOperationException>(() => browser.Configure("other/driver"));
    }

    [Fact]
    public async Task Two_windows_sharing_a_debugging_port_drive_distinct_grains_without_crosstalk()
    {
        var provider = new Provider();
        await using var brain = await Start(provider);
        await brain.Get<IWebBrowser>("workspace/first/browser").Configure("workspace/first/driver");
        await brain.Get<IWebBrowser>("workspace/second/browser").Configure("workspace/second/driver");
        var first = brain.Get<IWebBrowserConnector>("workspace/first/browser");
        var second = brain.Get<IWebBrowserConnector>("workspace/second/browser");
        var secondSession = new string('b', 32);
        await first.Connect(12345, Session);
        await second.Connect(12345, secondSession);
        await first.Disconnect(Session);
        Assert.False((await brain.Get<IPlaywright>("workspace/first/driver").Read()).Connected);
        Assert.Equal(secondSession, (await brain.Get<IPlaywright>("workspace/second/driver").Read()).SessionId);
        Assert.True((await brain.Get<IPlaywright>("workspace/second/driver").Read()).Connected);
        Assert.False(provider.Pages[1].Disposed);
    }

    [Fact]
    public async Task A_stale_disconnect_cannot_detach_a_replacement_session()
    {
        var provider = new Provider();
        await using var brain = await Start(provider);
        var connector = brain.Get<IWebBrowserConnector>("research/browser");
        await connector.Connect(12345, Session);
        await connector.Connect(12345, new string('b', 32));
        await connector.Disconnect(Session);
        Assert.True((await brain.Get<IPlaywright>("research/browser").Read()).Connected);
        Assert.True(provider.Pages[0].Disposed);
        Assert.False(provider.Pages[1].Disposed);
    }

    [Fact]
    public async Task The_declared_driver_survives_reactivation_but_the_local_attachment_does_not()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IWebBrowser>("research/browser");
        await browser.Configure("research/driver");
        var connector = brain.Get<IWebBrowserConnector>("research/browser");
        await connector.Connect(12345, Session);
        await brain.DeactivateAsync(browser, ct);
        Assert.False((await brain.Get<IPlaywright>("research/driver").Read()).Connected);
        await connector.Connect(12345, new string('b', 32));
        Assert.True((await brain.Get<IPlaywright>("research/driver").Read()).Connected);
        Assert.Equal(0, (await browser.Read()).Version);
        Assert.Empty((await browser.Read()).Uri);
    }

    [Fact]
    public async Task Disconnecting_during_attachment_prevents_a_late_ready_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var brain = await Start(provider);
        await brain.Get<IWebBrowser>("research/browser").Configure("research/driver", "research/status");
        var connector = brain.Get<IWebBrowserConnector>("research/browser");
        var connect = connector.Connect(12345, Session);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        var disconnect = connector.Disconnect(Session);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while ((await brain.Get<IPlaywright>("research/driver").Read()).SessionId is not null)
        { await Task.Delay(20, timeout.Token); }
        var page = new Page();
        provider.Pending.SetResult(page);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        await disconnect.WaitAsync(timeout.Token);
        Assert.True(page.Disposed);
        Assert.False((await brain.Get<IPlaywright>("research/driver").Read()).Connected);
        Assert.Equal("Browser disconnected", (await brain.Get<IText>("research/status").Read()).Markdown);
    }

    [Fact]
    public async Task Failed_attachment_reports_unavailable_and_can_be_retried()
    {
        var provider = new Provider { Fail = true };
        await using var brain = await Start(provider);
        await brain.Get<IWebBrowser>("research/browser").Configure("research/driver", "research/status");
        var connector = brain.Get<IWebBrowserConnector>("research/browser");
        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.Connect(12345, Session));
        Assert.Equal("Browser unavailable", (await brain.Get<IText>("research/status").Read()).Markdown);
        Assert.False((await brain.Get<IPlaywright>("research/driver").Read()).Ready);
        provider.Fail = false;
        await connector.Connect(12345, Session);
        Assert.Equal("Ready", (await brain.Get<IText>("research/status").Read()).Markdown);
    }

    [Fact]
    public void The_script_contract_and_status_signal_do_not_expose_the_debugging_port()
    {
        Assert.True(PlatformOnlyAttribute.AppliesTo(typeof(IWebBrowserConnector)));
        Assert.False(PlatformOnlyAttribute.AppliesTo(typeof(IWebBrowser)));
        Assert.DoesNotContain(typeof(IWebBrowser).GetMethods(), method => method.Name is "Connect" or "Disconnect");
        Assert.DoesNotContain(typeof(BrowserConnected).GetProperties(), property => property.Name == "Port");
        Assert.DoesNotContain(typeof(WebBrowserState).GetProperties(), property => property.Name == "Port");
    }

    [Fact]
    public async Task Navigating_publishes_the_uri()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var browser = brain.Get<IWebBrowser>("docs");
        await using var nav = await brain.Observe<BrowserNavigated>(browser, ct);
        await browser.Navigate("https://learn.microsoft.com/winui", "WinUI");
        Assert.Equal("https://learn.microsoft.com/winui", (await nav.NextAsync(ct: ct)).Uri);
        Assert.Equal("WinUI", (await browser.Read()).Title);
    }

    private sealed class Provider : IBrowserSessionProvider
    {
        public bool Fail { get; set; }
        public TaskCompletionSource<IBrowserPageSession>? Pending { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<BrowserAttachment> Attachments { get; } = [];
        public List<Page> Pages { get; } = [];
        public Task<IBrowserPageSession> AttachAsync(BrowserAttachment attachment, CancellationToken ct)
        {
            Attachments.Add(attachment);
            Started.TrySetResult();
            if (Fail) { throw new InvalidOperationException("Browser unavailable"); }
            if (Pending is not null) { return Pending.Task; }
            var page = new Page();
            Pages.Add(page);
            return Task.FromResult<IBrowserPageSession>(page);
        }
    }
    private sealed class Page : IBrowserPageSession
    {
        public bool FailDispose { get; set; }
        public TaskCompletionSource? PendingDispose { get; set; }
        public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public bool Connected => !Disposed;
        public string? Url => "https://example.com";
        public string? Title => "Example";
        public Task<BrowserObservation> SnapshotAsync(CancellationToken ct) => Task.FromResult(new BrowserObservation(Url!, Title!, "", []));
        public Task<BrowserObservation> NavigateAsync(string url, CancellationToken ct) => SnapshotAsync(ct);
        public Task<BrowserObservation> ClickAsync(string selector, CancellationToken ct) => SnapshotAsync(ct);
        public Task<BrowserObservation> FillAsync(string selector, string value, CancellationToken ct) => SnapshotAsync(ct);
        public async ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult();
            if (FailDispose) { throw new InvalidOperationException("Detachment failed."); }
            if (PendingDispose is { } pending) { await pending.Task; }
            Disposed = true;
        }
    }
}
