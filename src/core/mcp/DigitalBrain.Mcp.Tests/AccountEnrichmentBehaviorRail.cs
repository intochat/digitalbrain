using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Behaviors;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class AccountEnrichmentBehaviorRail(IntegrationsFixture fixture)
{
    [Fact(DisplayName =
        "AccountEnrichment process and behavior rail share scripted Gmail/Salesforce edges end-to-end")]
    public async Task ProcessAndBehaviorRailShareScriptedEdges()
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
        test.Mcp().Catalog(
            IntegrationsFixture.SalesforceServerKey,
            AdmittedMcpTools.SalesforceUpdateAccount(),
            AdmittedMcpTools.SalesforceSoqlQuery(
                IntegrationsFixture.SampleAccountId,
                IntegrationsFixture.SampleEnrichmentDescription));

        var rail = test.Neuron<IBehaviorNeuron>("com.digitalbrain.account-enrichment");
        var proposed = await rail.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            AccountEnrichmentRailProgram.Source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["account-enrichment"] = AccountEnrichmentRailProgram.Feature,
            },
            "Account Enrichment",
            "Gmail to Salesforce enrichment"));
        await rail.Reference.RunTests(new RunBehaviorTests(CommandId.New(), proposed.ProposedArtifactHash!));
        var approval = new BehaviorRevisionApproval(
            Guid.NewGuid(),
            CommandId.New(),
            proposed.ProposedArtifactHash!,
            IntegrationsFixture.SessionOf(test),
            test.Clock.UtcNow);
        var delivered = rail.Incoming.NextAsync<BehaviorRevisionApproval>(cancellationToken);
        await test.Client.SendAsync(rail.Id, approval);
        _ = await delivered;
        await rail.Reference.Approve(approval);
        await rail.Reference.Activate(new ActivateBehaviorRevision(CommandId.New(), proposed.ProposedArtifactHash!));

        var enrichment = test.Neuron<IAccountEnrichment>("enricher");
        var commandId = CommandId.New();
        var proposedWait = enrichment.Outgoing.NextAsync<AccountEnrichmentProposed>(cancellationToken);
        await test.Client.SendAsync<IAccountEnrichment>(
            "enricher",
            new EnrichAccountFromEmail(
                commandId,
                IntegrationsFixture.SampleMessageId,
                IntegrationsFixture.SampleAccountId,
                IntegrationsFixture.SampleGmailAccount));
        var enrichmentProposed = (await proposedWait).Synapse;

        var sfApproval = IntegrationsFixture.Approval(test, commandId, enrichmentProposed.Fingerprint);
        var completedWait = enrichment.Outgoing.NextAsync<AccountEnriched>(cancellationToken);
        await test.Client.SendAsync(enrichment.Id, sfApproval);
        var completed = (await completedWait).Synapse;
        Assert.Equal(IntegrationsFixture.SampleAccountId, completed.AccountId);

        var executed = await rail.Reference.Execute(new ExecuteBehaviorRevision(
            CommandId.New(),
            "EnrichTrigger",
            $$"""{"MessageId":"{{IntegrationsFixture.SampleMessageId}}","AccountId":"{{IntegrationsFixture.SampleAccountId}}","GmailAccount":"{{IntegrationsFixture.SampleGmailAccount}}"}"""));
        Assert.True(executed.Succeeded, executed.Outcome);
        Assert.Contains(IntegrationsFixture.SampleAccountId, executed.Outcome, StringComparison.Ordinal);
        Assert.True(test.Mcp().SessionCount >= 2);
    }
}

internal static class AccountEnrichmentRailProgram
{
    public const string Feature =
        """
        Feature: account enrichment
          Scenario: enrich account from email
            Given a gmail message and salesforce account
            When the enrichment behavior runs
            Then the account description is proposed for approval
        """;

    public const string Source =
        """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Abstractions;
        using DigitalBrain.Behaviors;

        public sealed record EnrichTrigger(string MessageId, string AccountId, string GmailAccount) : Synapse;

        public sealed class AccountEnrichmentProgram : IBehaviorProgram<EnrichTrigger>
        {
            public ValueTask ExecuteAsync(EnrichTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
            {
                context.SetState(
                    "outcome",
                    $"enriched:{trigger.AccountId}:{trigger.MessageId}:{trigger.GmailAccount}");
                return ValueTask.CompletedTask;
            }
        }

        public sealed class AccountEnrichmentInstallTests : IBehaviorInstallTests
        {
            public ValueTask<BehaviorInstallTestReport> RunAsync(
                IBehaviorContext context,
                IReadOnlyDictionary<string, string> features,
                CancellationToken cancellationToken)
                => ValueTask.FromResult(BehaviorInstallTestReport.Pass(1, "account-enrichment"));
        }
        """;
}
