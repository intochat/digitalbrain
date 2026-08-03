using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

internal static class GmailHelpers
{
    internal static void CatalogSampleMessage(TestBrain test)
    {
        ArgumentNullException.ThrowIfNull(test);
        IntegrationsGmailHosts.GmailHost.SeedMessage(
            IntegrationsFixture.SampleMessageId,
            IntegrationsFixture.SampleSubject,
            IntegrationsFixture.SampleSender,
            IntegrationsFixture.SampleBody);
    }

    internal static void ScriptReadSampleMessage(TestBrain test, string messageId = IntegrationsFixture.SampleMessageId)
    {
        test.PlannerChat().ReplyWithCapabilityCall(
            IntegrationsFixture.GmailGetMessageTool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = messageId,
                ["format"] = "FULL",
            });
        test.PlannerChat().Reply("done");
    }

    internal static async Task SeedAuthorizationAsync(
        TestBrain test,
        string account = IntegrationsFixture.SampleGmailAccount,
        CancellationToken cancellationToken = default)
    {
        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>("mcp");
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var parked = test.Client.Get<IGmail>(account)
            .SendAsync(new GmailRequest("Seed Google authorization", commandId), hang.Token);

        var required = (await requiredWait).Synapse;
        Assert.Equal(IntegrationsFixture.GmailServerKey, required.ServerKey);

        _ = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(required.State, "test-auth-code", Error: null, Iss: null),
            cancellationToken);

        await hang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parked);

        test.PlannerChat().Reply("seed complete");
        var seeded = await test.Client.Get<IGmail>(account)
            .SendAsync(new GmailRequest("Seed Google authorization", commandId), cancellationToken);
        Assert.True(seeded.Succeeded, seeded.Error);
        test.PlannerChat().Reset();
    }

    internal static async Task<GmailResponse> SendReadIntentAsync(
        TestBrain test,
        CommandId commandId,
        string account,
        string messageId,
        CancellationToken cancellationToken)
    {
        CatalogSampleMessage(test);
        await SeedAuthorizationAsync(test, account, cancellationToken);
        ScriptReadSampleMessage(test, messageId);
        return await test.Client.Get<IGmail>(account)
            .SendAsync(
                new GmailRequest($"Read Gmail message {messageId}", commandId),
                cancellationToken);
    }
}
