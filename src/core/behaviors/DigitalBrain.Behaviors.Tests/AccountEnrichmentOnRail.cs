using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class AccountEnrichmentOnRail(BehaviorsFixture fixture)
{
    [Fact(DisplayName =
        "AccountEnrichment behavior rides the rail propose→compile→BDD→approve→activate→execute")]
    public async Task AccountEnrichmentBehaviorRidesTheRail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var rail = test.Neuron<IBehaviorNeuron>(BehaviorsFixture.AccountEnrichmentBehavior);

        var proposedWait = rail.Outgoing.NextAsync<BehaviorRevisionProposed>(cancellationToken);
        var compileWait = rail.Outgoing.NextAsync<BehaviorCompileSucceeded>(cancellationToken);
        var proposed = await rail.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            RailPrograms.AccountEnrichmentProgram(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["account-enrichment"] = RailPrograms.AccountEnrichmentFeature,
            },
            "Account Enrichment",
            "Gmail to Salesforce enrichment"));

        Assert.Equal(BehaviorRevisionStatus.Proposed, proposed.Status);
        Assert.Equal(proposed.ProposedArtifactHash, (await proposedWait).Synapse.ArtifactHash);
        Assert.Equal(proposed.ProposedArtifactHash, (await compileWait).Synapse.ArtifactHash);

        var testsWait = rail.Outgoing.NextAsync<BehaviorTestsPassed>(cancellationToken);
        var tested = await rail.Reference.RunTests(
            new RunBehaviorTests(CommandId.New(), proposed.ProposedArtifactHash!));
        Assert.True(tested.TestsPassed);
        Assert.Equal(proposed.ProposedArtifactHash, (await testsWait).Synapse.ArtifactHash);

        var approval = new BehaviorRevisionApproval(
            Guid.NewGuid(),
            CommandId.New(),
            proposed.ProposedArtifactHash!,
            ISessionNeuron.ForOwner(test.Client.Owner),
            test.Clock.UtcNow);
        var delivered = rail.Incoming.NextAsync<BehaviorRevisionApproval>(cancellationToken);
        await test.Client.SendAsync(rail.Id, approval, cancellationToken);
        _ = await delivered;
        var approvedWait = rail.Outgoing.NextAsync<BehaviorRevisionApproved>(cancellationToken);
        var approved = await rail.Reference.Approve(approval);
        Assert.True(approved.IsApproved);
        Assert.Equal(proposed.ProposedArtifactHash, (await approvedWait).Synapse.ArtifactHash);

        var activatedWait = rail.Outgoing.NextAsync<BehaviorRevisionActivated>(cancellationToken);
        var active = await rail.Reference.Activate(
            new ActivateBehaviorRevision(CommandId.New(), proposed.ProposedArtifactHash!));
        Assert.Equal(proposed.ProposedArtifactHash, active.ActiveArtifactHash);
        Assert.Equal(proposed.ProposedArtifactHash, (await activatedWait).Synapse.ArtifactHash);

        var executedWait = rail.Outgoing.NextAsync<BehaviorExecuted>(cancellationToken);
        var executed = await rail.Reference.Execute(new ExecuteBehaviorRevision(
            CommandId.New(),
            "EnrichTrigger",
            """{"MessageId":"msg-enrich-1","AccountId":"001xx000003DGbYAAW","GmailAccount":"reader@example.com"}"""));
        Assert.True(executed.Succeeded);
        Assert.Contains("001xx000003DGbYAAW", executed.Outcome, StringComparison.Ordinal);
        Assert.Equal(proposed.ProposedArtifactHash, (await executedWait).Synapse.ArtifactHash);
    }
}
