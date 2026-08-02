using DigitalBrain.Google;
using DigitalBrain.Mcp.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

[Collection(GmailFakeHostTestGroup.Name)]
public sealed class GmailOps(IntegrationsFixture fixture)
{
    [Fact(DisplayName = "GmailSearchRequest returns bounded headers without a model call")]
    public async Task SearchReturnsHeadersWithoutModel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        IntegrationsGmailHosts.GmailHost.Clear();
        GmailHelpers.CatalogSampleMessage(test);
        await GmailHelpers.SeedAuthorizationAsync(test, cancellationToken: cancellationToken);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(new GmailSearchRequest("from:ops", maxResults: 5), cancellationToken);

        Assert.True(response.Succeeded, response.Error);
        var header = Assert.Single(response.Headers);
        Assert.Equal(IntegrationsFixture.SampleMessageId, header.Id);
        Assert.Equal(IntegrationsFixture.SampleSubject, header.Subject);
        Assert.Equal(IntegrationsFixture.SampleSender, header.Sender);
        Assert.Equal(0, test.PlannerChat().CallCount);
    }

    [Fact(DisplayName = "GmailGetMessageRequest returns a bounded GmailMessage without a model call")]
    public async Task GetMessageReturnsBodyWithoutModel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailHelpers.CatalogSampleMessage(test);
        await GmailHelpers.SeedAuthorizationAsync(test, cancellationToken: cancellationToken);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(new GmailGetMessageRequest(IntegrationsFixture.SampleMessageId), cancellationToken);

        Assert.True(response.Succeeded, response.Error);
        Assert.NotNull(response.Message);
        Assert.Equal(IntegrationsFixture.SampleMessageId, response.Message.Id);
        Assert.Equal(IntegrationsFixture.SampleBody, response.Message.PlaintextBody);
        Assert.Equal(0, test.PlannerChat().CallCount);
    }
}
