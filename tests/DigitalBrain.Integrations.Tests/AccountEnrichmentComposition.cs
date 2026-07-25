using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class AccountEnrichmentComposition(IntegrationsFixture fixture)
{
    private const string AccountId = "001xx000003DGbYAAW";
    private const string GmailAccount = "reader@example.com";
    private const string MessageId = "msg-enrich-1";
    private const string Subject = "Acme pipeline update";
    private const string Sender = "ops@acme.example";
    private const string Body = "Q3 forecast closed green.";

    [Fact(DisplayName =
        "IAccountEnrichment composes Gmail→propose→session approval→AccountEnriched on scripted MCP edges")]
    public async Task EnrichmentCompletesThroughGmailProposeAndSessionApproval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var expectedDescription = $"Email from {Sender}: {Subject}\n{Body}";

        test.Mcp().Catalog(
            "google.gmail",
            AdmittedMcpTools.GmailGetMessage(
                id: MessageId,
                subject: Subject,
                sender: Sender,
                plaintextBody: Body));
        test.Mcp().Catalog(
            "salesforce",
            AdmittedMcpTools.SalesforceUpdateAccount(),
            AdmittedMcpTools.SalesforceSoqlQuery(AccountId, expectedDescription));

        var enrichment = test.Neuron<IAccountEnrichment>("enricher");
        var commandId = CommandId.New();
        var proposedWait = enrichment.Outgoing.NextAsync<AccountEnrichmentProposed>(
            cancellationToken);

        await test.Client.SendAsync<IAccountEnrichment>(
            "enricher",
            new EnrichAccountFromEmail(
                commandId,
                MessageId,
                AccountId,
                GmailAccount));

        var proposed = (await proposedWait).Synapse;
        Assert.Equal(commandId, proposed.CommandId);
        Assert.Equal(MessageId, proposed.MessageId);
        Assert.Equal(AccountId, proposed.AccountId);
        Assert.Equal(expectedDescription, proposed.Description);
        Assert.False(string.IsNullOrWhiteSpace(proposed.Fingerprint));

        var approval = new SalesforceMutationApproval(
            Guid.NewGuid(),
            commandId,
            proposed.Fingerprint,
            SessionOf(test),
            test.Clock.UtcNow);
        var approvalDelivered = enrichment.Incoming.NextAsync<SalesforceMutationApproval>(
            cancellationToken);
        var completedWait = enrichment.Outgoing.NextAsync<AccountEnriched>(cancellationToken);

        await test.Client.SendAsync(enrichment.Id, approval);

        Assert.Equal(approval, (await approvalDelivered).Synapse);

        var completed = (await completedWait).Synapse;
        Assert.Equal(commandId, completed.CommandId);
        Assert.Equal(MessageId, completed.MessageId);
        Assert.Equal(AccountId, completed.AccountId);
        Assert.Equal(expectedDescription, completed.Description);
        Assert.True(test.Mcp().SessionCount >= 2);
    }

    private static NeuronId SessionOf(TestBrain test)
        => new(ISessionNeuron.GrainTypeName, test.Client.Owner, "session");
}
