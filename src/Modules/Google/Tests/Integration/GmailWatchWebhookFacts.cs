using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DigitalBrain.Google;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GmailWatchWebhookFacts
{
    [Fact]
    public async Task GmailWatchHttpPublishesMailReceived()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntegrationTest.Create().WithModule<GoogleModule>()
            .StartAsync(ct);
        var gmail = brain.Get<IGmail>("user@gmail.com");
        await using var mail = await brain.Observe<MailReceived>(gmail, ct);
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { emailAddress = "user@gmail.com", historyId = "123" })));
        using var response = await brain.HttpClient.PostAsJsonAsync(
            "/google/gmail/watch",
            new { message = new { data, messageId = "m1" }, subscription = "projects/x/subscriptions/gmail" },
            ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("123", (await mail.NextAsync(ct: ct)).HistoryId);
    }
}
