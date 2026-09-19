using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DigitalBrain.E2ETesting;
using DigitalBrain.Google;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GmailWatchWebhookFacts
{
    [Fact]
    public async Task GmailWatchHttpPublishesMailReceived()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new() { Modules = [new GoogleModule()] }, ct);
        await using var web = await ModuleWebHost.StartAsync(brain.Cluster(), [new GoogleModule()], ct);
        await using var mail = await brain.Observe<MailReceived>(brain.Get<IGmail>("user@gmail.com"), ct);
        var response = await web.Client.PostAsJsonAsync("google/gmail/watch", GmailWatchHttp.GmailPubSubBody("123", "user@gmail.com"), ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("123", (await mail.NextAsync(ct: ct)).HistoryId);
    }

    [Fact]
    public async Task GmailOAuthCallbackWithFakeCodePublishesGmailConnected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new()
        {
            Modules = [new GoogleModule()],
            ConfigureSilo = silo => silo.Services.AddSingleton<IGmailTokenExchange>(new FakeGmailTokens()),
        }, ct);
        await using var web = await ModuleWebHost.StartAsync(brain.Cluster(), [new GoogleModule()], ct);
        await using var connected = await brain.Observe<GmailConnected>(brain.Get<IGmail>("gmail"), ct);
        var response = await web.Client.GetAsync("google/gmail/oauth/callback?code=fake", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user@gmail.com", (await connected.NextAsync(ct: ct)).EmailAddress);
    }
}

file static class GmailWatchHttp
{
    public static object GmailPubSubBody(string historyId, string emailAddress)
    {
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { emailAddress, historyId })));
        return new { message = new { data, messageId = "m1" }, subscription = "projects/x/subscriptions/gmail" };
    }
}

internal sealed class FakeGmailTokens : IGmailTokenExchange
{
    public Task<GmailTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<GmailTokenGrant> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken)
        => Task.FromResult(new GmailTokenGrant("access-token", "refresh-token", GmailOAuthConfiguration.ReadScope, 3600, "user@gmail.com"));
}
