using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Salesforce;
using DigitalBrain.Shell;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

[Collection(GmailFakeHostTestGroup.Name)]
public sealed class AccountEnrichmentProcessSample(IntegrationsFixture fixture)
{
    [Fact(DisplayName =
        "IAccountEnrichment processes Gmail→propose→session approval→AccountEnriched on scripted MCP edges")]
    public async Task EnrichmentCompletesThroughGmailProposeAndSessionApproval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        await RunEnrichmentToCompletionAsync(test, cancellationToken);
        Assert.True(test.Mcp().SessionCount >= 2);
    }

    [Fact(DisplayName =
        "multi-module enrichment then OS enrichment scene journals without secrets")]
    public async Task EnrichmentThenOSSurfaceOpensEnrichmentScene()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(IntegrationsFixture.ShellName);
        var expectedDescription = IntegrationsFixture.SampleEnrichmentDescription;

        var completed = await RunEnrichmentToCompletionAsync(test, cancellationToken);
        Assert.Equal(expectedDescription, completed.Description);

        var homeOpened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal("home", homeOpened.Synapse.SceneKey);

        await test.Client.SendAsync<IShell>(
            IntegrationsFixture.ShellName,
            new OpenScene(
                CommandId.New(),
                IntegrationsFixture.EnrichmentSceneKey,
                IntegrationsFixture.EnrichmentSceneTitle),
            cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(IntegrationsFixture.EnrichmentSceneKey, opened.Synapse.SceneKey);
        Assert.Equal(IntegrationsFixture.EnrichmentSceneTitle, opened.Synapse.Title);
        Assert.DoesNotContain("token", opened.Synapse.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(expectedDescription, opened.Synapse.Title, StringComparison.Ordinal);
    }

    private static async Task<AccountEnriched> RunEnrichmentToCompletionAsync(TestBrain test, CancellationToken cancellationToken)
    {
        var expectedDescription = IntegrationsFixture.SampleEnrichmentDescription;
        GmailHelpers.CatalogSampleMessage(test);
        await GmailHelpers.SeedAuthorizationAsync(test, cancellationToken: cancellationToken);
        test.Mcp().Catalog(
            IntegrationsFixture.SalesforceServerKey,
            AdmittedMcpTools.SalesforceUpdateAccount(),
            AdmittedMcpTools.SalesforceSoqlQuery(IntegrationsFixture.SampleAccountId, expectedDescription));
        GmailHelpers.ScriptReadSampleMessage(test);

        var enrichment = test.Neuron<IAccountEnrichment>("enricher");
        var commandId = CommandId.New();
        var proposedWait = enrichment.Outgoing.NextAsync<AccountEnrichmentProposed>(cancellationToken);

        await test.Client.SendAsync<IAccountEnrichment>(
            "enricher",
            new EnrichAccountFromEmail(
                commandId,
                IntegrationsFixture.SampleMessageId,
                IntegrationsFixture.SampleAccountId,
                IntegrationsFixture.SampleGmailAccount),
            cancellationToken);

        var proposed = (await proposedWait).Synapse;
        Assert.Equal(commandId, proposed.CommandId);
        Assert.Equal(expectedDescription, proposed.Description);

        var approval = IntegrationsFixture.Approval(test, commandId, proposed.Fingerprint);
        var completedWait = enrichment.Outgoing.NextAsync<AccountEnriched>(cancellationToken);

        var approved = await SalesforceHelpers.ApproveAsync(test, approval, cancellationToken);
        Assert.True(approved.Succeeded);
        Assert.Equal(SalesforceMutationState.Completed, approved.Mutation!.State);

        await test.Client.SendAsync(enrichment.Id, approval, cancellationToken);
        return (await completedWait).Synapse;
    }
}
