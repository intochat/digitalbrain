using System.Net;
using DigitalBrain.Google.Gmail;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Google.Gmail.Tests.E2E;

[Collection(GmailHostCollection.Name)]
public sealed class GmailConfiguredRegistrationFacts(GmailHostFixture host)
{
    [Fact(Timeout = 180_000)]
    public async Task AuthorizationCodeCallbackPublishesGmailConnected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        var gmail = brain.Get<IGmail>("gmail");
        await using var connected = await brain.Observe<GmailConnected>(gmail, ct);
        using var response = await brain.HttpClient.GetAsync("google/gmail/oauth/callback?code=fake-code", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("gmail", (await connected.NextAsync(ct: ct)).EmailAddress);
    }
}
