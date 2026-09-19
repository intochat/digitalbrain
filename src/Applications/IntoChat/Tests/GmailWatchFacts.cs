using System.Net.Http.Json;

namespace IntoChat.Tests;

public sealed class GmailWatchFacts
{
    [Fact]
    public async Task GmailWatchReturnsAccepted()
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
        using var response = await http.PostAsJsonAsync(
            "/google/gmail/watch",
            new { HistoryId = "123", EmailAddress = "user@gmail.com" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
