using DigitalBrain.Google;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class GmailReadMessage(IntegrationsFixture fixture)
{
    [Fact(DisplayName =
        "IGmail.ReadMessage returns GmailMessage vocabulary on the scripted edge")]
    public async Task ReadMessageReturnsGmailMessageVocabulary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(
            IntegrationsFixture.GmailServerKey,
            AdmittedMcpTools.GmailGetMessage(
                id: IntegrationsFixture.SampleMessageId,
                subject: IntegrationsFixture.SampleSubject,
                sender: IntegrationsFixture.SampleSender,
                plaintextBody: IntegrationsFixture.SampleBody));

        var driver = test.Neuron<IIntegrationDriver>("gmail-driver");
        var message = await driver.Reference.ReadGmailMessage(
            IntegrationsFixture.SampleGmailAccount,
            IntegrationsFixture.SampleMessageId,
            cancellationToken);

        Assert.Equal(
            new GmailMessage(
                IntegrationsFixture.SampleMessageId,
                IntegrationsFixture.SampleSubject,
                IntegrationsFixture.SampleSender,
                IntegrationsFixture.SampleBody),
            message);
    }

    [Fact(DisplayName =
        "IGmail.ReadMessage fails closed when the scripted edge is not admitted")]
    public async Task ReadMessageFailsClosedWhenEdgeIsNotAdmitted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(IntegrationsFixture.GmailServerKey, AdmittedMcpTools.GmailGetMessageWithIncompatibleAnnotations());

        var driver = test.Neuron<IIntegrationDriver>("gmail-refuse");
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            driver.Reference.ReadGmailMessage(
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

        Assert.Contains("incompatible with the admitted", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gmail", failure.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "IGmail.ReadMessage rejects a Gmail response for a different requested message")]
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

        var driver = test.Neuron<IIntegrationDriver>("gmail-mismatch");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.Reference.ReadGmailMessage(
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

        Assert.Equal(
            "Gmail get_message returned id 'msg-different' for requested message 'msg-enrich-1'.",
            failure.Message);
    }

    [Fact(DisplayName =
        "IGmail.ReadMessage rejects a Gmail response that omits its message identifier")]
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

        var driver = test.Neuron<IIntegrationDriver>("gmail-missing-id");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.Reference.ReadGmailMessage(
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

        Assert.Equal("Gmail get_message returned no id.", failure.Message);
    }

    [Fact(DisplayName =
        "IGmail.ReadMessage returns the requested Gmail message when its subject and body are empty")]
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

        var driver = test.Neuron<IIntegrationDriver>("gmail-empty-content");
        var message = await driver.Reference.ReadGmailMessage(
            IntegrationsFixture.SampleGmailAccount,
            IntegrationsFixture.SampleMessageId,
            cancellationToken);

        Assert.Equal(
            new GmailMessage(
                IntegrationsFixture.SampleMessageId,
                string.Empty,
                IntegrationsFixture.SampleSender,
                string.Empty),
            message);
    }

    [Fact(DisplayName =
        "IGmail.ReadMessage rejects an admitted Gmail MCP tool error")]
    public async Task ReadMessageRejectsToolError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(IntegrationsFixture.GmailServerKey, AdmittedMcpTools.GmailGetMessageWithToolError());

        var driver = test.Neuron<IIntegrationDriver>("gmail-tool-error");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.Reference.ReadGmailMessage(
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

        Assert.Equal("DigitalBrain Gmail MCP tool 'get_message' reported an error.", failure.Message);
    }
}
