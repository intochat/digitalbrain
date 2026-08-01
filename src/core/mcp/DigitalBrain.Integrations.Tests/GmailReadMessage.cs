using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Mcp.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class GmailReadMessage(IntegrationsFixture fixture)
{
    [Fact(DisplayName =
        "GmailRequest returns GmailMessage vocabulary on the scripted edge")]
    public async Task ReadMessageReturnsGmailMessageVocabulary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailHelpers.CatalogSampleMessage(test);

        var response = await GmailHelpers.SendReadIntentAsync(
            test,
            CommandId.New(),
            IntegrationsFixture.SampleGmailAccount,
            IntegrationsFixture.SampleMessageId,
            cancellationToken);

        Assert.Equal(
            new GmailMessage(
                IntegrationsFixture.SampleMessageId,
                IntegrationsFixture.SampleSubject,
                IntegrationsFixture.SampleSender,
                IntegrationsFixture.SampleBody),
            Assert.Single(response.Messages));
    }

    [Fact(DisplayName =
        "GmailRequest fails closed when the scripted edge is not admitted")]
    public async Task ReadMessageFailsClosedWhenEdgeIsNotAdmitted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(IntegrationsFixture.GmailServerKey, AdmittedMcpTools.GmailGetMessageWithIncompatibleAnnotations());
        test.PlannerChat().Reply("no tools");

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}"),
                cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Contains("admitted", response.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gmail", response.Error, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "GmailRequest rejects a Gmail response for a different requested message")]
    public async Task ReadMessageRejectsMismatchedResponseId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(
            IntegrationsFixture.GmailServerKey,
            AdmittedMcpTools.GmailGetMessage(
                id: "msg-different",
                subject: IntegrationsFixture.SampleSubject,
                sender: IntegrationsFixture.SampleSender,
                plaintextBody: IntegrationsFixture.SampleBody));
        GmailHelpers.ScriptReadSampleMessage(test, IntegrationsFixture.SampleMessageId);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}"),
                cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Equal(
            "Gmail get_message returned id 'msg-different' for requested message 'msg-enrich-1'.",
            response.Error);
    }

    [Fact(DisplayName =
        "GmailRequest rejects a Gmail response that omits its message identifier")]
    public async Task ReadMessageRejectsMissingResponseId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(
            IntegrationsFixture.GmailServerKey,
            AdmittedMcpTools.GmailGetMessageWithPayload(new
            {
                subject = IntegrationsFixture.SampleSubject,
                sender = IntegrationsFixture.SampleSender,
                plaintextBody = IntegrationsFixture.SampleBody,
            }));
        GmailHelpers.ScriptReadSampleMessage(test, IntegrationsFixture.SampleMessageId);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}"),
                cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Equal("Gmail get_message returned no id.", response.Error);
    }

    [Fact(DisplayName =
        "GmailRequest returns the requested Gmail message when its subject and body are empty")]
    public async Task ReadMessageAllowsEmptySubjectAndBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(
            IntegrationsFixture.GmailServerKey,
            AdmittedMcpTools.GmailGetMessage(
                id: IntegrationsFixture.SampleMessageId,
                subject: string.Empty,
                sender: IntegrationsFixture.SampleSender,
                plaintextBody: string.Empty));

        var response = await GmailHelpers.SendReadIntentAsync(
            test,
            CommandId.New(),
            IntegrationsFixture.SampleGmailAccount,
            IntegrationsFixture.SampleMessageId,
            cancellationToken);

        Assert.Equal(
            new GmailMessage(
                IntegrationsFixture.SampleMessageId,
                string.Empty,
                IntegrationsFixture.SampleSender,
                string.Empty),
            Assert.Single(response.Messages));
    }

    [Fact(DisplayName =
        "GmailRequest rejects an admitted Gmail MCP tool error")]
    public async Task ReadMessageRejectsToolError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(IntegrationsFixture.GmailServerKey, AdmittedMcpTools.GmailGetMessageWithToolError());
        GmailHelpers.ScriptReadSampleMessage(test, IntegrationsFixture.SampleMessageId);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}"),
                cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Equal("DigitalBrain Gmail MCP tool 'get_message' reported an error.", response.Error);
    }
}
