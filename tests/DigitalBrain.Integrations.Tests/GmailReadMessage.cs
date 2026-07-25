using DigitalBrain.Google;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class GmailReadMessage(IntegrationsFixture fixture)
{
    [Fact(DisplayName =
        "IGmail.ReadMessage admits get_message on the scripted MCP edge and returns GmailMessage")]
    public async Task ReadMessageReturnsAdmittedStructuredContent()
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

        Assert.Equal(IntegrationsFixture.SampleMessageId, message.Id);
        Assert.Equal(IntegrationsFixture.SampleSubject, message.Subject);
        Assert.Equal(IntegrationsFixture.SampleSender, message.Sender);
        Assert.Equal(IntegrationsFixture.SampleBody, message.PlaintextBody);
        Assert.True(test.Mcp().SessionCount >= 1);
    }

    [Fact(DisplayName =
        "IGmail.ReadMessage refuses get_message when admitted annotations fail on the scripted MCP edge")]
    public async Task ReadMessageRefusesIncompatibleToolAnnotations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(
            IntegrationsFixture.GmailServerKey,
            AdmittedMcpTools.GmailGetMessageWithIncompatibleAnnotations());

        var driver = test.Neuron<IIntegrationDriver>("gmail-refuse");
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            driver.Reference.ReadGmailMessage(
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

        Assert.Contains("incompatible with the admitted", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(IntegrationsFixture.GmailGetMessageTool, failure.Message, StringComparison.Ordinal);
        Assert.True(test.Mcp().SessionCount >= 1);
    }
}
