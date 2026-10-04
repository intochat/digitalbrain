using System.Text.Json;
using DigitalBrain.Microsoft.Playwright;

namespace DigitalBrain.Modules.Microsoft.Playwright.Tests;

public sealed class VisibleBrowserFacts
{
    [Fact]
    public async Task Existing_native_page_navigates_and_survives_detach()
    {
        var portText = Environment.GetEnvironmentVariable("DIGITALBRAIN_PLAYWRIGHT_CDP_PORT");
        var id = Environment.GetEnvironmentVariable("DIGITALBRAIN_PLAYWRIGHT_SESSION");
        if (!int.TryParse(portText, out var port) || string.IsNullOrWhiteSpace(id))
        {
            Assert.Skip("Set DIGITALBRAIN_PLAYWRIGHT_CDP_PORT and DIGITALBRAIN_PLAYWRIGHT_SESSION for a marked native WebView2 page.");
            return;
        }
        var ct = TestContext.Current.CancellationToken;
        var provider = new PlaywrightSessionProvider();
        await using var owner = new BrowserSessionOwner(provider);
        await owner.Attach(new(port, id), ct);
        Assert.Equal($"about:blank#digitalbrain-{id}", owner.Read().Url);
        var result = await owner.Navigate("https://example.com", ct);
        Assert.NotEmpty(result.Title);
        Assert.Contains("documentation", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.Url, owner.Read().Url);
        Assert.Equal(result.Url, (await owner.Snapshot(ct)).Url);
        await owner.Detach(id, ct);
        Assert.False(owner.Read().Connected);
        using var http = new HttpClient();
        var targets = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", ct);
        using var json = JsonDocument.Parse(targets);
        Assert.Contains(json.RootElement.EnumerateArray(), item => item.GetProperty("url").GetString() == result.Url);
        // Reattach the exact previously validated target even after it has left its initial marker.
        await owner.Attach(new(port, id), ct);
        Assert.Equal(result.Url, (await owner.Snapshot(ct)).Url);

        // A missing selector keeps a real SDK action pending until user Stop cancels it.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var waitingAction = owner.Click("#digitalbrain-intentionally-missing-smoke-target", stop.Token);
        Assert.False(waitingAction.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingAction);
        Assert.False(owner.Read().Connected);
        await owner.Attach(new(port, id), ct);
        Assert.Equal(result.Url, (await owner.Snapshot(ct)).Url);
    }
}
