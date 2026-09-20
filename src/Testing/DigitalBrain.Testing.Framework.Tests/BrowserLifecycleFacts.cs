using DigitalBrain.Testing.E2E;
using Microsoft.Playwright;

namespace DigitalBrain.Tests;

public sealed class BrowserLifecycleFacts
{
    [Fact]
    public async Task CanceledAcquisitionDisposesItsLateResult()
    {
        using var cancel = new CancellationTokenSource();
        var result = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquiring = E2EBrain.AcquireAsync(result.Task, _ => { released.TrySetResult(); return Task.CompletedTask; }, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquiring);
        result.SetResult(new object());
        await released.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReadinessUsesDeclaredSelectorWithoutTransientPlaceholder()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.RouteAsync("https://fixture.test/**", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = "<html><body><div id='ready'>Ready</div></body></html>",
        }));
        await E2EBrain.PreparePageAsync(page, new("https://fixture.test/"), "#ready", () => 2_000,
            TestContext.Current.CancellationToken);
        Assert.Equal("Ready", await page.Locator("#ready").InnerTextAsync());
    }

    [Fact]
    public async Task CancellationClosesContextAndDisposalIsIdempotent()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var context = await browser.NewContextAsync();
        using var cancel = new CancellationTokenSource();
        var session = new BrowserSession(context, null, cancel.Token) { Page = await context.NewPageAsync() };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Close += (_, _) => closed.TrySetResult();
        var navigating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await session.Page.RouteAsync("https://fixture.test/**", _ =>
        {
            navigating.TrySetResult();
            return Task.CompletedTask;
        });
        var preparation = E2EBrain.PreparePageAsync(session.Page, new("https://fixture.test/"), "#ready", () => 10_000, cancel.Token);
        await navigating.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancel.CancelAsync();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<PlaywrightException>(() => preparation.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        await session.DisposeAsync();
        await session.DisposeAsync();
        Assert.True(session.Page.IsClosed);
    }

    [Fact]
    public async Task TraceIsSavedEvenWhenPageIsAlreadyClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "browser-artifacts-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var context = await browser.NewContextAsync();
            await context.Tracing.StartAsync(new() { Snapshots = true });
            var session = new BrowserSession(context, directory, TestContext.Current.CancellationToken)
                { Page = await context.NewPageAsync() };
            await session.Page.EvaluateAsync("console.error('synthetic-secret-never-log')");
            await session.Page.CloseAsync();
            await session.DisposeAsync();
            Assert.Single(Directory.GetFiles(directory, "*.zip"));
            var diagnostics = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory, "*.log")), TestContext.Current.CancellationToken);
            Assert.Contains("console:error", diagnostics);
            Assert.DoesNotContain("synthetic-secret-never-log", diagnostics);
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
    }
}
