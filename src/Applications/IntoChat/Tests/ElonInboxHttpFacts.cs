using System.Net.Http.Json;

namespace IntoChat.Tests;

public sealed class ElonInboxHttpFacts
{
    [Fact]
    public async Task WebhookFillsUiInbox()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = await E2EDigitalBrain.StartAsync<Projects.IntoChat_AppHost>(
            new E2EOptions
            {
                Args = ["DigitalBrain:Flutter:Hosting:Kind=None"],
                WaitFor = ["IntoChat"],
            },
            ct);
        using var http = app.CreateHttpClient("IntoChat");
        using var webhook = await http.PostAsJsonAsync(
            "/twitter/webhook",
            new { Account = "elonmusk", Text = "Bitcoin to the moon" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, webhook.StatusCode);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!deadline.Token.IsCancellationRequested)
        {
            var lines = await http.GetFromJsonAsync<List<string>>("/ui/inbox", deadline.Token) ?? [];
            if (lines.Exists(line => line.Contains("Bitcoin to the moon", StringComparison.Ordinal)))
            {
                return;
            }
            await Task.Delay(200, deadline.Token);
        }
        Assert.Fail("GET /ui/inbox never contained the Bitcoin tweet.");
    }
}
