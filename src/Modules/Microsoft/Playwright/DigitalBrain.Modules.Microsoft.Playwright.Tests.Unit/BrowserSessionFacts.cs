using DigitalBrain.Microsoft.Playwright;
namespace DigitalBrain.Tests;
public sealed class BrowserSessionFacts
{
    [Fact]
    public async Task Stale_detach_preserves_replacement_page()
    {
        var provider = new Provider();
        await using var owner = new BrowserSessionOwner(provider);
        await owner.Attach(new(1234, new string('a', 32)), TestContext.Current.CancellationToken);
        await owner.Attach(new(1234, new string('b', 32)), TestContext.Current.CancellationToken);
        await owner.Detach(new string('a', 32), TestContext.Current.CancellationToken);
        Assert.True(owner.Read().Connected);
        Assert.Equal(new string('b', 32), owner.Read().SessionId);
        Assert.True(provider.Pages[0].Disposed);
        Assert.False(provider.Pages[1].Disposed);
    }
    [Fact]
    public async Task Cancellation_during_attachment_disposes_late_result()
    {
        var ready = new TaskCompletionSource<IBrowserPageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new BrowserSessionOwner(new DelayedProvider(ready.Task));
        using var ct = new CancellationTokenSource();
        var attach = owner.Attach(new(1234, new string('a', 32)), ct.Token);
        ct.Cancel();
        var page = new Page();
        ready.SetResult(page);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attach);
        await owner.DisposeAsync();
        Assert.True(page.Disposed);
        Assert.False(owner.Read().Connected);
    }
    [Fact]
    public async Task Detach_cancels_active_operation_and_reports_disconnected()
    {
        var provider = new Provider();
        await using var owner = new BrowserSessionOwner(provider);
        var id = new string('a', 32);
        await owner.Attach(new(1234, id), TestContext.Current.CancellationToken);
        var action = owner.Navigate("https://example.com", TestContext.Current.CancellationToken);
        await provider.Pages[0].Started.Task;
        await owner.Detach(id, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action);
        Assert.False(owner.Read().Connected);
    }
    [Theory]
    [InlineData("file:///C:/secret")]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://192.168.1.2")]
    [InlineData("http://[::1]")]
    [InlineData("https://example.com:8443")]
    [InlineData("https://user:password@example.com")]
    [InlineData("http://printer")]
    public void Rejects_non_public_navigation(string url) => Assert.Throws<ArgumentException>(() => PublicBrowserNetwork.PublicUri(url));

    [Fact]
    public void Target_selection_ignores_other_windows_and_rejects_duplicate_markers()
    {
        var id = new string('c', 32);
        var marker = $"about:blank#digitalbrain-{id}";
        Assert.Equal(marker, PlaywrightSessionProvider.SelectTarget<string>(["about:blank", marker, "https://example.com"], value => value, id));
        Assert.Throws<InvalidOperationException>(() => PlaywrightSessionProvider.SelectTarget<string>(["about:blank"], value => value, id));
        Assert.Throws<InvalidOperationException>(() => PlaywrightSessionProvider.SelectTarget<string>([marker, marker], value => value, id));
    }
    [Fact]
    public async Task Disposing_owner_cancels_pending_attach_and_disposes_late_page()
    {
        var ready = new TaskCompletionSource<IBrowserPageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new BrowserSessionOwner(new DelayedProvider(ready.Task));
        var attach = owner.Attach(new(1234, new string('a', 32)), TestContext.Current.CancellationToken);
        var disposing = owner.DisposeAsync().AsTask();
        Assert.False(owner.Read().Connected);
        var page = new Page();
        ready.SetResult(page);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attach);
        await disposing;
        Assert.True(page.Disposed);
    }
    [Fact]
    public async Task Operations_are_serialized_while_read_remains_available()
    {
        var provider = new Provider();
        await using var owner = new BrowserSessionOwner(provider);
        await owner.Attach(new(1234, new string('a', 32)), TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var first = owner.Navigate("https://example.com", cancellation.Token);
        await provider.Pages[0].Started.Task;
        var second = owner.Snapshot(TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);
        Assert.True(owner.Read().Connected);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal("Example", (await second).Title);
    }
    [Fact]
    public async Task Separate_owners_do_not_detach_each_other()
    {
        var provider = new Provider();
        await using var first = new BrowserSessionOwner(provider);
        await using var second = new BrowserSessionOwner(provider);
        await first.Attach(new(1234, new string('a', 32)), TestContext.Current.CancellationToken);
        await second.Attach(new(1234, new string('b', 32)), TestContext.Current.CancellationToken);
        await first.DisposeAsync();
        Assert.True(second.Read().Connected);
        await provider.Pages[1].DisposeAsync();
        Assert.False(second.Read().Connected);
    }
    [Fact]
    public void Exhausted_activity_cannot_poison_the_next_activity()
    {
        var previous = new BrowserActivityBudget();
        Assert.False(previous.TryReceive(128 * 1024 * 1024 + 1));
        for (var index = 0; index < 2000; index++) { Assert.True(previous.TryRequest()); }
        Assert.False(previous.TryRequest());
        var next = new BrowserActivityBudget();
        Assert.True(next.TryRequest());
        Assert.True(next.TryReceive(1024));
        Assert.False(previous.TryReceive(1024));
        Assert.True(next.TryReceive(1024));
    }
    private sealed class DelayedProvider(Task<IBrowserPageSession> task) : IBrowserSessionProvider
    {
        public Task<IBrowserPageSession> AttachAsync(BrowserAttachment attachment, CancellationToken ct) => task;
    }
    private sealed class Provider : IBrowserSessionProvider
    {
        public List<Page> Pages { get; } = [];
        public Task<IBrowserPageSession> AttachAsync(BrowserAttachment attachment, CancellationToken ct)
        {
            var page = new Page(); Pages.Add(page);
            return Task.FromResult<IBrowserPageSession>(page);
        }
    }
    private sealed class Page : IBrowserPageSession
    {
        public bool Disposed { get; private set; }
        public bool Connected => !Disposed;
        public string? Url => "https://example.com";
        public string? Title => "Example";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BrowserObservation> NavigateAsync(string url, CancellationToken ct)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        }
        public Task<BrowserObservation> SnapshotAsync(CancellationToken ct) => Task.FromResult(new BrowserObservation(Url!, Title!, "", []));
        public Task<BrowserObservation> ClickAsync(string selector, CancellationToken ct) => SnapshotAsync(ct);
        public Task<BrowserObservation> FillAsync(string selector, string value, CancellationToken ct) => SnapshotAsync(ct);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
