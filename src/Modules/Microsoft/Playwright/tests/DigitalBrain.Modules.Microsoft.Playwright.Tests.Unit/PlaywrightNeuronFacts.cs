using DigitalBrain.Microsoft.Playwright;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace DigitalBrain.Modules.Microsoft.Playwright.Tests.Unit;

public sealed class PlaywrightNeuronFacts
{
    [Fact]
    public async Task Attaching_publishes_ready_from_the_browser_and_deactivation_releases_the_page()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IPlaywright>("browser");
        await using var ready = await brain.Observe<BrowserReady>(browser, ct);
        await browser.Attach(new(1234, new string('a', 32)), ct);
        var signal = await ready.NextAsync(ct: ct);
        Assert.Equal(browser.GetGrainId().ToString(), signal.Publisher);
        Assert.Equal(new string('a', 32), signal.SessionId);
        Assert.True((await browser.Read()).Ready);
        await brain.DeactivateAsync(browser, ct);
        Assert.True(provider.Pages[0].Disposed);
        Assert.False((await browser.Read()).Connected);
    }

    [Fact]
    public async Task Disconnect_interrupts_navigation_and_publishes_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IPlaywright>("browser");
        await using var unavailable = await brain.Observe<BrowserUnavailable>(browser, ct);
        var id = new string('a', 32);
        await browser.Attach(new(1234, id), ct);
        var navigation = browser.Navigate("https://example.com", ct);
        await provider.Pages[0].Started.Task.WaitAsync(ct);
        await browser.Detach(id, ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation);
        Assert.Equal(id, (await unavailable.NextAsync(ct: ct)).SessionId);
        Assert.False((await browser.Read()).Ready);
        Assert.True(provider.Pages[0].Disposed);
    }

    [Fact]
    public async Task A_second_connect_supersedes_an_attachment_still_in_progress()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider { BlockFirst = true };
        await using var brain = await Start(provider);
        var browser = brain.Get<IPlaywright>("browser");
        await using var ready = await brain.Observe<BrowserReady>(browser, ct);
        var first = browser.Attach(new(1234, new string('a', 32)), ct);
        await provider.Started.Task.WaitAsync(ct);
        var second = browser.Attach(new(1234, new string('b', 32)), ct);
        await second;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await browser.Detach(new string('a', 32), ct);
        Assert.Equal(new string('b', 32), (await ready.NextAsync(ct: ct)).SessionId);
        Assert.Equal(new string('b', 32), (await browser.Read()).SessionId);
        Assert.Single(ready.Snapshot);
    }

    [Fact]
    public async Task A_pending_connection_rejects_actions_without_canceling_the_attachment()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var brain = await Start(provider);
        var browser = brain.Get<IPlaywright>("browser");
        var attach = browser.Attach(new(1234, new string('a', 32)), ct);
        await provider.Started.Task.WaitAsync(ct);
        await Assert.ThrowsAsync<BrowserNotConnectedException>(() => browser.Snapshot(ct));
        Assert.Equal(new string('a', 32), (await browser.Read()).SessionId);
        provider.Release.SetResult();
        await attach;
        Assert.True((await browser.Read()).Ready);
    }

    [Fact]
    public async Task Disconnect_during_attachment_never_publishes_ready()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider { BlockFirst = true };
        await using var brain = await Start(provider);
        var browser = brain.Get<IPlaywright>("browser");
        await using var ready = await brain.Observe<BrowserReady>(browser, ct);
        await using var unavailable = await brain.Observe<BrowserUnavailable>(browser, ct);
        var id = new string('a', 32);
        var attach = browser.Attach(new(1234, id), ct);
        await provider.Started.Task.WaitAsync(ct);
        await browser.Detach(id, ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attach);
        Assert.Empty(ready.Snapshot);
        Assert.Equal(id, (await unavailable.NextAsync(ct: ct)).SessionId);
        Assert.False((await browser.Read()).Ready);
    }

    [Fact]
    public async Task Repeated_connect_does_not_interrupt_navigation()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Provider();
        await using var brain = await Start(provider);
        var browser = brain.Get<IPlaywright>("browser");
        var attachment = new BrowserAttachment(1234, new string('a', 32));
        await browser.Attach(attachment, ct);
        var navigation = browser.Navigate("https://example.com", ct);
        await provider.Pages[0].Started.Task.WaitAsync(ct);
        await browser.Attach(attachment, ct);
        Assert.Single(provider.Pages);
        Assert.False(navigation.IsCompleted);
        await browser.Detach(attachment.SessionId, ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation);
    }

    [Fact]
    public async Task An_unattached_browser_fails_with_a_catchable_error()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new Provider());
        var browser = brain.Get<IPlaywright>("browser");
        await Assert.ThrowsAsync<BrowserNotConnectedException>(() => browser.Navigate("https://example.com", ct));
        await Assert.ThrowsAsync<BrowserNotConnectedException>(() => browser.Snapshot(ct));
    }

    [Fact]
    public async Task Scripted_observations_and_requested_urls_survive_reactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new Provider());
        var scripted = brain.Get<IScriptedBrowser>("scenario");
        BrowserObservation[] pages = [new("https://example.com", "First", "one", [new("Next", "https://example.com/next")]),
            new("https://example.com/next", "Second", "two", [])];
        await scripted.Script(pages);
        IPlaywright browser = scripted;
        Assert.Equal("First", (await browser.Navigate("https://example.com", ct)).Title);
        await brain.DeactivateAsync(scripted, ct);
        Assert.Equal("Second", (await browser.Snapshot(ct)).Title);
        Assert.Equal(["https://example.com"], await scripted.RequestedUrls());
        Assert.True((await browser.Read()).Ready);
        await Assert.ThrowsAsync<InvalidOperationException>(() => browser.Snapshot(ct));
        await scripted.Script(pages);
        Assert.Empty(await scripted.RequestedUrls());
        Assert.Single((await browser.Snapshot(ct)).Links);
    }

    private static Task<UnitBrain> Start(Provider provider) => UnitTest.Create().WithModule<PlaywrightModule>()
        .ConfigureSilo(silo => silo.Services.AddSingleton<IBrowserSessionProvider>(provider))
        .StartAsync(TestContext.Current.CancellationToken);

    private sealed class Provider : IBrowserSessionProvider
    {
        public bool BlockFirst;
        public TaskCompletionSource? Release;
        public List<Page> Pages { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IBrowserPageSession> AttachAsync(BrowserAttachment attachment, CancellationToken ct)
        {
            Started.TrySetResult();
            if (Release is { } release) { await release.Task.WaitAsync(ct); }
            if (BlockFirst && attachment.SessionId == new string('a', 32)) { await Task.Delay(Timeout.Infinite, ct); }
            var page = new Page();
            Pages.Add(page);
            return page;
        }
    }
    private sealed class Page : IBrowserPageSession
    {
        public bool Disposed;
        public bool Connected => !Disposed;
        public string? Url => "https://example.com";
        public string? Title => "Example";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BrowserObservation> NavigateAsync(string url, CancellationToken ct)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new(url, "", "", []);
        }
        public Task<BrowserObservation> SnapshotAsync(CancellationToken ct) => Task.FromResult(new BrowserObservation(Url!, Title!, "", []));
        public Task<BrowserObservation> ClickAsync(string selector, CancellationToken ct) => SnapshotAsync(ct);
        public Task<BrowserObservation> FillAsync(string selector, string value, CancellationToken ct) => SnapshotAsync(ct);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
