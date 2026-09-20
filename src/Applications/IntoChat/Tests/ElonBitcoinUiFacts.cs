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
        var execution = new TestExecutionOptions
        {
            ArtifactDirectory = Path.Combine(AppContext.BaseDirectory, "e2e-artifacts", "elon-ui"),
        };
        await using var brain = await E2EDigitalBrainSimulation.StartAsync<Projects.IntoChat_AppHost>(
            new IntoChatOptions
            {
                Flutter = new() { Hosting = new() { Kind = FlutterHostKind.Web } }
            },
            execution,
            ct);
        await brain.Get<IBitcoin>("btc").SetPrice(64_000);
        await using var browser = await brain.OpenBrowserAsync(ct);
        using var webhook = await brain.HttpClient.PostAsJsonAsync(
            "/twitter/webhook",
            new { Account = "elonmusk", Text = "Bitcoin to the moon" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, webhook.StatusCode);

        // Flutter web renders to canvas. Reload the shell with the semantics opt-in so
        // its text reaches the DOM for Playwright's native locator assertions.
        var semanticsUrl = browser.Page.Url + (browser.Page.Url.Contains('?') ? "&" : "?") + "semantics=true";
        await browser.Page.GotoAsync(semanticsUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var placeholder = browser.Page.Locator("flt-semantics-placeholder");
        await placeholder.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 60_000 });
        await placeholder.EvaluateAsync("element => element.click()");
        await browser.Page.Locator("flt-semantics")
            .First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 30_000 });

        await Assertions.Expect(browser.Page.GetByText("elonmusk: Bitcoin to the moon  BTC 64000", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
    }
}
