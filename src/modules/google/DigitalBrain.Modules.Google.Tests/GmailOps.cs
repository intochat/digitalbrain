using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Mcp.Testing;
using Xunit;

namespace DigitalBrain.Google.Tests;

public sealed class GmailOps(GoogleFixture fixture)
{
    [Fact(DisplayName = "GmailSearchRequest returns bounded headers without a model call")]
    public async Task Search_returns_headers_without_model()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailTestHosts.GmailHost.SeedMessage(
            GoogleFixture.SampleMessageId,
            GoogleFixture.SampleSubject,
            GoogleFixture.SampleSender,
            GoogleFixture.SampleBody);
        await GmailAuth.SeedAsync(test, GoogleFixture.GmailAccount, cancellationToken);

        var response = await test.Client.Get<IGmail>(GoogleFixture.GmailAccount)
            .SendAsync(new GmailSearchRequest("from:ops", maxResults: 5), cancellationToken);

        Assert.True(response.Succeeded, response.Error);
        var header = Assert.Single(response.Headers);
        Assert.Equal(GoogleFixture.SampleMessageId, header.Id);
        Assert.Equal(GoogleFixture.SampleSubject, header.Subject);
        Assert.Equal(GoogleFixture.SampleSender, header.Sender);
        Assert.Equal(0, test.PlannerChat().CallCount);
    }

    [Fact(DisplayName = "GmailGetMessageRequest returns a bounded GmailMessage without a model call")]
    public async Task Get_message_returns_body_without_model()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailTestHosts.GmailHost.SeedMessage(
            GoogleFixture.SampleMessageId,
            GoogleFixture.SampleSubject,
            GoogleFixture.SampleSender,
            GoogleFixture.SampleBody);
        await GmailAuth.SeedAsync(test, GoogleFixture.GmailAccount, cancellationToken);

        var response = await test.Client.Get<IGmail>(GoogleFixture.GmailAccount)
            .SendAsync(new GmailGetMessageRequest(GoogleFixture.SampleMessageId), cancellationToken);

        Assert.True(response.Succeeded, response.Error);
        Assert.NotNull(response.Message);
        Assert.Equal(GoogleFixture.SampleMessageId, response.Message.Id);
        Assert.Equal(GoogleFixture.SampleBody, response.Message.PlaintextBody);
        Assert.Equal(0, test.PlannerChat().CallCount);
    }
}
