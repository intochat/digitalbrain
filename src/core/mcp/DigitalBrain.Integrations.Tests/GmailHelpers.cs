using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;

namespace DigitalBrain.Integrations.Tests;

internal static class GmailHelpers
{
    internal static void CatalogSampleMessage(TestBrain test)
        => test.Mcp().Catalog(
            IntegrationsFixture.GmailServerKey,
            AdmittedMcpTools.GmailGetMessage(
                id: IntegrationsFixture.SampleMessageId,
                subject: IntegrationsFixture.SampleSubject,
                sender: IntegrationsFixture.SampleSender,
                plaintextBody: IntegrationsFixture.SampleBody));

    internal static void ScriptReadSampleMessage(TestBrain test, string messageId = IntegrationsFixture.SampleMessageId)
    {
        test.PlannerChat().ReplyWithCapabilityCall(
            IntegrationsFixture.GmailGetMessageTool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["messageId"] = messageId,
                ["messageFormat"] = "FULL_CONTENT",
            });
        test.PlannerChat().Reply("done");
    }

    internal static Task<GmailResponse> SendReadIntentAsync(
        TestBrain test,
        CommandId commandId,
        string account,
        string messageId,
        CancellationToken cancellationToken)
    {
        ScriptReadSampleMessage(test, messageId);
        return test.Client.Get<IGmail>(account)
            .SendAsync(
                new GmailRequest($"Read Gmail message {messageId}", commandId),
                cancellationToken);
    }
}
