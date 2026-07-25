using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Flutter;
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

        await RunEnrichmentToCompletionAsync(test, expectedDescription, cancellationToken);
        Assert.True(test.Mcp().SessionCount >= 2);
    }

    [Fact(DisplayName =
        "multi-module enrichment then OS enrichment scene journals without secrets")]
    public async Task EnrichmentThenOsSurfaceOpensEnrichmentScene()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var expectedDescription = $"Email from {Sender}: {Subject}\n{Body}";
        var shell = test.Neuron<IShell>("desk");

        var completed = await RunEnrichmentToCompletionAsync(
            test,
            expectedDescription,
            cancellationToken);
        Assert.Equal(expectedDescription, completed.Description);

        // Same product sentence as AccountEnrichmentSurface — open via Flutter vocabulary only
        // so Integrations.Tests stays free of AI.Contracts (ChatMessage codec) load.
        await shell.Reference.Open(new OpenScene(
            CommandId.New(),
            "enrichment",
            "Account enrichment"));

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal("enrichment", opened.Synapse.SceneKey);
        Assert.Equal("Account enrichment", opened.Synapse.Title);
        Assert.DoesNotContain("token", opened.Synapse.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(expectedDescription, opened.Synapse.Title, StringComparison.Ordinal);
    }

    private static async Task<AccountEnriched> RunEnrichmentToCompletionAsync(
        TestBrain test,
        string expectedDescription,
        CancellationToken cancellationToken)
    {
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
        Assert.Equal(expectedDescription, proposed.Description);

        var approval = new SalesforceMutationApproval(
            Guid.NewGuid(),
            commandId,
            proposed.Fingerprint,
            SessionOf(test),
            test.Clock.UtcNow);
        var completedWait = enrichment.Outgoing.NextAsync<AccountEnriched>(cancellationToken);

        await test.Client.SendAsync(enrichment.Id, approval);
        return (await completedWait).Synapse;
    }

    private static NeuronId SessionOf(TestBrain test)
        => new(ISessionNeuron.GrainTypeName, test.Client.Owner, "session");
}
