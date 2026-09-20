using DigitalBrain.Flutter;
using DigitalBrain.Behaviors;
using System.Net.Http.Json;
using Microsoft.Playwright;

namespace IntoChat.Tests;

public sealed class ElonBitcoinUiFacts
{
    [Fact(Timeout = 240_000)]
    public async Task WebhookTweetAppearsInFlutterUi()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.StartAsync<Projects.IntoChat_AppHost>(new() { Application = new DigitalBrainConfiguration { Flutter = new() { Hosting = new() { Kind = DigitalBrain.Flutter.FlutterHostKind.Web } } } }, ct);
        await brain.Get<IBitcoin>("btc").SetPrice(64_000);
        await using var browser = await brain.OpenBrowserAsync(ct);
        using var webhook = await brain.HttpClient.PostAsJsonAsync(
            "/twitter/webhook",
            new { Account = "elonmusk", Text = "Bitcoin to the moon" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, webhook.StatusCode);

        await Assertions.Expect(browser.Page.GetByText("elonmusk: Bitcoin to the moon  BTC 64000", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
    }
}
