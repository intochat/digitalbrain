using DigitalBrain.Google;
using DigitalBrain.Testing;
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
        Assert.Contains("Gmail", failure.Message, StringComparison.Ordinal);
    }
}
