using DigitalBrain.Core;
using DigitalBrain.Kernel.SelfEvolution;
using DigitalBrain.Tests.TestSupport;
using Orleans.Runtime;

namespace DigitalBrain.Tests.Kernel;

#pragma warning disable ORLEANSEXP005

[Trait("Category", "cluster")]
[Collection(OrleansJournalClusterCollection.Name)]
public sealed class SelfEvolutionDurabilityTests(OrleansJournalClusterFixture fixture)
{
    [Fact]
    public async Task Proposal_Replays_As_Pending_After_Grain_Reactivation()
    {
        DurableRecordingApplyHandler.Clear();

        var grain = fixture.Cluster.Client.GetGrain<ISelfEvolutionNeuron>(
            "self-evolution-durable-pending-" + Guid.NewGuid().ToString("N"));
        var proposalId = "durable-pending-" + Guid.NewGuid().ToString("N");

        await grain.DeliverAsync(Proposal(proposalId));

        await ReactivateAsync(grain);
        Assert.Contains((await grain.GetIncomingTimelineAsync()).OfType<SelfEvolutionProposal>(), proposal =>
            proposal.ProposalId == proposalId);

        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: false, DecidedBy: "user:owner", Reason: "deny after replay"));

        var timeline = await grain.GetOutgoingTimelineAsync();
        Assert.Contains(timeline.OfType<SelfEvolutionDecisionRecorded>(), decision =>
            decision.ProposalId == proposalId && !decision.Approved);
    }

    [Fact]
    public async Task Decision_Replays_As_Decided_After_Grain_Reactivation()
    {
        DurableRecordingApplyHandler.Clear();

        var grain = fixture.Cluster.Client.GetGrain<ISelfEvolutionNeuron>(
            "self-evolution-durable-decision-" + Guid.NewGuid().ToString("N"));
        var proposalId = "durable-decision-" + Guid.NewGuid().ToString("N");

        await grain.DeliverAsync(Proposal(proposalId));
        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: false, DecidedBy: "user:owner", Reason: "deny"));

        await ReactivateAsync(grain);
        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: true, DecidedBy: "user:owner"));

        var timeline = await grain.GetOutgoingTimelineAsync();
        Assert.Single(timeline.OfType<SelfEvolutionDecisionRecorded>(), decision => decision.ProposalId == proposalId);
        Assert.Contains(timeline.OfType<SelfEvolutionDecisionRejected>(), rejected =>
            rejected.ProposalId == proposalId && rejected.Reason.Contains("already been decided", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Applied_Proposal_Is_Not_Reapplied_On_Replay()
    {
        DurableRecordingApplyHandler.Clear();

        var grain = fixture.Cluster.Client.GetGrain<ISelfEvolutionNeuron>(
            "self-evolution-durable-applied-" + Guid.NewGuid().ToString("N"));
        var proposalId = "durable-applied-" + Guid.NewGuid().ToString("N");

        await grain.DeliverAsync(Proposal(proposalId));
        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: true, DecidedBy: "user:owner"));
        Assert.Equal(1, DurableRecordingApplyHandler.Count(proposalId));

        await ReactivateAsync(grain);
        Assert.Equal(1, DurableRecordingApplyHandler.Count(proposalId));

        await grain.DeliverAsync(new SelfEvolutionDecision(proposalId, Approved: true, DecidedBy: "user:owner"));
        Assert.Equal(1, DurableRecordingApplyHandler.Count(proposalId));
        Assert.Contains((await grain.GetOutgoingTimelineAsync()).OfType<SelfEvolutionDecisionRejected>(), rejected =>
            rejected.ProposalId == proposalId);
    }

    private static SelfEvolutionProposal Proposal(string proposalId) => new(
        ProposalId: proposalId,
        Scope: "kernel",
        Rationale: "durability test",
        ProposedChange: "replay self-evolution audit",
        ApplyVia: DurableRecordingApplyHandler.ApplyViaId,
        Risk: SelfEvolutionRisk.KernelRestart,
        RequiresHumanApproval: true,
        RollbackPlan: "restore durable-checkpoint",
        Origin: "durability-test");

    private async Task ReactivateAsync(ISelfEvolutionNeuron grain)
    {
        await fixture.Cluster.DeactivateAsync((IAddressable)grain);
        await grain.GetOutgoingTimelineAsync();
    }
}

#pragma warning restore ORLEANSEXP005
