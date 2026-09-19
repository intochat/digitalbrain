using System.Net;
using System.Net.Http.Json;
using DigitalBrain.E2ETesting;
using DigitalBrain.Google;
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
        var response = await web.Client.PostAsJsonAsync("google/gmail/watch", new GmailWatchPush("123", "user@gmail.com"), ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("123", (await mail.NextAsync(ct: ct)).HistoryId);
    }

    [Fact]
    public async Task GmailOAuthCallbackReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new() { Modules = [new GoogleModule()] }, ct);
        await using var web = await ModuleWebHost.StartAsync(brain.Cluster(), [new GoogleModule()], ct);
        var response = await web.Client.GetAsync("google/gmail/oauth/callback", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
