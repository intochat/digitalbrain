using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

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
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { emailAddress = "user@gmail.com", historyId = "123" })));
        using var response = await http.PostAsJsonAsync(
            "/google/gmail/watch",
            new { message = new { data, messageId = "m1" }, subscription = "projects/x/subscriptions/gmail" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
