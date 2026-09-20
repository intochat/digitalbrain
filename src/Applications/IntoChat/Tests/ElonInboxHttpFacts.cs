using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Inbox;
using DigitalBrain.Behaviors;
using System.Net.Http.Json;

namespace IntoChat.Tests;

public sealed class ElonInboxHttpFacts
{
    [Fact]
    public async Task WebhookFillsUiInbox()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.StartAsync<Projects.IntoChat_AppHost>(DigitalBrainOptions.Headless, ct);
        await brain.Get<IBitcoin>("btc").SetPrice(64_000);
        using var webhook = await brain.HttpClient.PostAsJsonAsync(
            "/twitter/webhook",
            new { Account = "elonmusk", Text = "Bitcoin to the moon" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, webhook.StatusCode);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!deadline.Token.IsCancellationRequested)
        {
            var lines = await brain.HttpClient.GetFromJsonAsync<List<string>>("/ui/inbox", deadline.Token) ?? [];
            if (lines.Contains("elonmusk: Bitcoin to the moon  BTC 64000"))
            {
                return;
            }
            await Task.Delay(200, deadline.Token);
        }
        Assert.Fail("GET /ui/inbox never contained the Bitcoin tweet.");
    }
}
