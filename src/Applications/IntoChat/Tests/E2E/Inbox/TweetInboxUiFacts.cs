using DigitalBrain.Flutter;
using DigitalBrain.Behaviors;
using System.Net.Http.Json;
using Microsoft.Playwright;
using IntoChat.Tests.E2E.Workspace;

namespace IntoChat.Tests.E2E.Inbox;

public sealed class TweetInboxUiFacts
{
    [Fact(Timeout = 240_000)]
    public async Task WebhookTweetAppearsInFlutterUi()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.Create().ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp()).StartAsync(ct);
        await WorkspaceBrowser.CreateProjectAsync(brain.Page, "Tweet inbox");
        await brain.Get<IBitcoin>("btc").SetPrice(64_000);
        using var webhook = await brain.HttpClient.PostAsJsonAsync(
            "/twitter/webhook",
            new { Account = "elonmusk", Text = "Bitcoin to the moon" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, webhook.StatusCode);

        await Assertions.Expect(brain.Page.GetByText("elonmusk: Bitcoin to the moon  BTC 64000", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
    }
}
