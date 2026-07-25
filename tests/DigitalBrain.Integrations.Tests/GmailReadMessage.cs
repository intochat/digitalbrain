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
            "google.gmail",
            AdmittedMcpTools.GmailGetMessage(
                id: "msg-42",
                subject: "Pipeline status",
                sender: "ops@example.com",
                plaintextBody: "All green."));

        var driver = test.Neuron<IIntegrationDriver>("gmail-driver");
        var message = await driver.Reference.ReadGmailMessage(
            "reader@example.com",
            "msg-42",
            cancellationToken);

        Assert.Equal("msg-42", message.Id);
        Assert.Equal("Pipeline status", message.Subject);
        Assert.Equal("ops@example.com", message.Sender);
        Assert.Equal("All green.", message.PlaintextBody);
        Assert.True(test.Mcp().SessionCount >= 1);
    }

    [Fact(DisplayName =
        "IGmail.ReadMessage refuses get_message when admitted annotations fail on the scripted MCP edge")]
    public async Task ReadMessageRefusesIncompatibleToolAnnotations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Mcp().Catalog(
            "google.gmail",
            AdmittedMcpTools.GmailGetMessageWithIncompatibleAnnotations());

        var driver = test.Neuron<IIntegrationDriver>("gmail-refuse");
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            driver.Reference.ReadGmailMessage(
                "reader@example.com",
                "msg-bad",
                cancellationToken));

        Assert.Contains("incompatible with the admitted", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("get_message", failure.Message, StringComparison.Ordinal);
        Assert.True(test.Mcp().SessionCount >= 1);
    }
}

