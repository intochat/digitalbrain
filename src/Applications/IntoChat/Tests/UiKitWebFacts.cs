using DigitalBrain.Flutter;
using Microsoft.Playwright;
using System.Net.Http.Json;

namespace IntoChat.Tests;

public sealed class UiKitWebFacts
{
    [Fact(Timeout = 240_000)]
    public async Task ChartVideoAndBrowserAppearInFlutterUi()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.StartAsync<Projects.IntoChat_AppHost>(new() { Application = new DigitalBrainConfiguration { Flutter = new() { Hosting = new() { Kind = DigitalBrain.Flutter.FlutterHostKind.Web } } } }, ct);

        using var chart = await brain.HttpClient.PostAsJsonAsync("/ui/charts/e2e/render",
            new { title = "E2E BTC chart", kind = "line", points = new[] { new { eventId = "t0", label = "open", value = 64000 } } },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, chart.StatusCode);
        using var browserNav = await brain.HttpClient.PostAsJsonAsync("/ui/browsers/e2e/navigate",
            new { uri = "https://learn.microsoft.com/", title = "WinUI" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, browserNav.StatusCode);
        using var video = await brain.HttpClient.PostAsJsonAsync("/ui/videos/e2e/load",
            new { url = "https://example.com/intro.mp4", duration = 12 },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, video.StatusCode);

        await using var session = await brain.OpenBrowserAsync(ct);

        await Assertions.Expect(session.Page.GetByText("E2E BTC chart"))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(session.Page.GetByText("WinUI"))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(session.Page.GetByText("https://example.com/intro.mp4"))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
    }
}
