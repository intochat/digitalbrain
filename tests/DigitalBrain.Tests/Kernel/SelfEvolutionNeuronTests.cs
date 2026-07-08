using DigitalBrain.Core;
using DigitalBrain.Kernel.SelfEvolution;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Kernel;

public sealed class SelfEvolutionNeuronTests : NeuronTestBase
{
    private readonly RecordingApplyHandler _handler = new("test.apply", SelfEvolutionRisk.KernelRestart);
    private readonly RecordingApplyHandler _lowRiskHandler = new("low-risk.apply", SelfEvolutionRisk.InProcessCode);
    private readonly FailingApplyHandler _failingHandler = new();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder.ConfigureServices(services =>
    {
        services.AddSingleton<ISelfEvolutionApplyHandler>(_handler);
        services.AddSingleton<ISelfEvolutionApplyHandler>(_lowRiskHandler);
        services.AddSingleton<ISelfEvolutionApplyHandler>(_failingHandler);
    });

    [Fact]
    public async Task Proposal_Is_Journaled_As_Pending()
    {
        var neuron = Grain<ISelfEvolutionNeuron>("self-evolution-pending");

        await neuron.DeliverAsync(Proposal("pending-1"));

        var outgoing = await neuron.GetOutgoingTimelineAsync();
        Assert.Contains(outgoing.OfType<SelfEvolutionProposalPending>(), pending =>
            pending.ProposalId == "pending-1" && pending.ApplyVia == "test.apply");
    }

    [Fact]
    public async Task Rejected_Decision_Does_Not_Call_Apply_Handler()
    {
        var neuron = Grain<ISelfEvolutionNeuron>("self-evolution-reject");

        await neuron.DeliverAsync(Proposal("reject-1"));
        await neuron.DeliverAsync(new SelfEvolutionDecision("reject-1", Approved: false, DecidedBy: "user:owner", Reason: "not worth it"));

        Assert.Empty(_handler.Applied);
        Assert.Empty(_lowRiskHandler.Applied);
        var outgoing = await neuron.GetOutgoingTimelineAsync();
        Assert.Contains(outgoing.OfType<SelfEvolutionDecisionRecorded>(), decision =>
            decision.ProposalId == "reject-1" && !decision.Approved);
        Assert.DoesNotContain(outgoing.OfType<SelfEvolutionApplyResult>(), result => result.ProposalId == "reject-1");
    }

    [Fact]
    public async Task Approved_Decision_Calls_Matching_Handler_Once_And_Journals_Result()
    {
        var neuron = Grain<ISelfEvolutionNeuron>("self-evolution-approve");

        await neuron.DeliverAsync(Proposal("approve-1"));
        await neuron.DeliverAsync(new SelfEvolutionDecision("approve-1", Approved: true, DecidedBy: "user:owner"));

        Assert.Equal(["approve-1"], _handler.Applied);
        Assert.Empty(_lowRiskHandler.Applied);
        var outgoing = await neuron.GetOutgoingTimelineAsync();
        Assert.Contains(outgoing.OfType<SelfEvolutionApplyResult>(), result =>
            result.ProposalId == "approve-1" && result.Succeeded && result.ApplyVia == "test.apply");
    }

    [Fact]
    public async Task Expired_Proposal_Cannot_Be_Approved()
    {
        var neuron = Grain<ISelfEvolutionNeuron>("self-evolution-expired");

        await neuron.DeliverAsync(Proposal("expired-1", expiresAt: DateTimeOffset.UtcNow.AddMilliseconds(-1)));
        await neuron.DeliverAsync(new SelfEvolutionDecision("expired-1", Approved: true, DecidedBy: "user:owner"));

        Assert.Empty(_handler.Applied);
        Assert.Empty(_lowRiskHandler.Applied);
        var outgoing = await neuron.GetOutgoingTimelineAsync();
        Assert.Contains(outgoing.OfType<SelfEvolutionProposalExpired>(), expired => expired.ProposalId == "expired-1");
        Assert.Contains(outgoing.OfType<SelfEvolutionDecisionRejected>(), rejected => rejected.ProposalId == "expired-1");
    }

    [Fact]
    public async Task Unknown_ApplyVia_Cannot_Be_Applied()
    {
        var neuron = Grain<ISelfEvolutionNeuron>("self-evolution-unknown");

        await neuron.DeliverAsync(Proposal("unknown-1", applyVia: "missing.apply"));
        await neuron.DeliverAsync(new SelfEvolutionDecision("unknown-1", Approved: true, DecidedBy: "user:owner"));

        Assert.Empty(_handler.Applied);
        Assert.Empty(_lowRiskHandler.Applied);
        var outgoing = await neuron.GetOutgoingTimelineAsync();
        Assert.Contains(outgoing.OfType<SelfEvolutionApplyResult>(), result =>
            result.ProposalId == "unknown-1"
            && !result.Succeeded
            && result.Details.Contains("No self-evolution apply handler", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handler_Risk_Limit_Blocks_Higher_Risk_Proposal()
    {
        var neuron = Grain<ISelfEvolutionNeuron>("self-evolution-risk");

        await neuron.DeliverAsync(Proposal("risk-1", risk: SelfEvolutionRisk.KernelRestart, applyVia: "low-risk.apply"));
        await neuron.DeliverAsync(new SelfEvolutionDecision("risk-1", Approved: true, DecidedBy: "user:owner"));

        Assert.Empty(_handler.Applied);
        Assert.Empty(_lowRiskHandler.Applied);
        var outgoing = await neuron.GetOutgoingTimelineAsync();
        Assert.Contains(outgoing.OfType<SelfEvolutionApplyResult>(), result =>
            result.ProposalId == "risk-1"
            && !result.Succeeded
            && result.Details.Contains("allows InProcessCode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_Approved_Apply_With_Checkpoint_Journals_Rollback_Required()
    {
        var neuron = Grain<ISelfEvolutionNeuron>("self-evolution-rollback-required");

        await neuron.DeliverAsync(Proposal(
            "rollback-required-1",
            applyVia: FailingApplyHandler.ApplyViaId,
            risk: SelfEvolutionRisk.KernelRestart));
        await neuron.DeliverAsync(new SelfEvolutionDecision("rollback-required-1", Approved: true, DecidedBy: "user:owner"));

        var outgoing = await neuron.GetOutgoingTimelineAsync();
        Assert.Contains(outgoing.OfType<SelfEvolutionApplyResult>(), result =>
            result.ProposalId == "rollback-required-1"
            && !result.Succeeded
            && result.RollbackCheckpointId == "checkpoint-failed");
        Assert.Contains(outgoing.OfType<SelfEvolutionRollbackRequired>(), rollback =>
            rollback.ProposalId == "rollback-required-1"
            && rollback.ApplyVia == FailingApplyHandler.ApplyViaId
            && rollback.CheckpointId == "checkpoint-failed");
    }
    [Fact]
    public async Task Duplicate_Decisions_Do_Not_Double_Apply()
    {
        var neuron = Grain<ISelfEvolutionNeuron>("self-evolution-duplicate");

        await neuron.DeliverAsync(Proposal("duplicate-1"));
        await neuron.DeliverAsync(new SelfEvolutionDecision("duplicate-1", Approved: true, DecidedBy: "user:owner"));
        await neuron.DeliverAsync(new SelfEvolutionDecision("duplicate-1", Approved: true, DecidedBy: "user:owner"));

        Assert.Equal(["duplicate-1"], _handler.Applied);
        Assert.Empty(_lowRiskHandler.Applied);
        var outgoing = await neuron.GetOutgoingTimelineAsync();
        Assert.Single(outgoing.OfType<SelfEvolutionApplyResult>(), result => result.ProposalId == "duplicate-1" && result.Succeeded);
        Assert.Contains(outgoing.OfType<SelfEvolutionDecisionRejected>(), rejected => rejected.ProposalId == "duplicate-1");
    }

    [Fact]
    public async Task Invalid_Proposal_Is_Rejected_And_Not_Pending()
    {
        var neuron = Grain<ISelfEvolutionNeuron>("self-evolution-invalid");

        await neuron.DeliverAsync(Proposal("invalid-1", rollbackPlan: ""));
        await neuron.DeliverAsync(new SelfEvolutionDecision("invalid-1", Approved: true, DecidedBy: "user:owner"));

        Assert.Empty(_handler.Applied);
        Assert.Empty(_lowRiskHandler.Applied);
        var outgoing = await neuron.GetOutgoingTimelineAsync();
        Assert.Contains(outgoing.OfType<SelfEvolutionProposalRejected>(), rejected => rejected.ProposalId == "invalid-1");
        Assert.DoesNotContain(outgoing.OfType<SelfEvolutionProposalPending>(), pending => pending.ProposalId == "invalid-1");
    }

    private static SelfEvolutionProposal Proposal(
        string id,
        string applyVia = "test.apply",
        SelfEvolutionRisk risk = SelfEvolutionRisk.InProcessCode,
        DateTimeOffset? expiresAt = null,
        string rollbackPlan = "restore checkpoint") =>
        new(
            ProposalId: id,
            Scope: "kernel",
            Rationale: "test",
            ProposedChange: "change",
            ApplyVia: applyVia,
            Risk: risk,
            RequiresHumanApproval: true,
            RollbackPlan: rollbackPlan,
            Origin: "test",
            ExpiresAt: expiresAt);

    private sealed class FailingApplyHandler : ISelfEvolutionApplyHandler
    {
        public const string ApplyViaId = "failing.apply";
        public string ApplyVia => ApplyViaId;
        public SelfEvolutionRisk MaxRisk => SelfEvolutionRisk.KernelRestart;

        public Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct) =>
            Task.FromResult(new SelfEvolutionApplyResult(
                proposal.ProposalId,
                proposal.ApplyVia,
                Succeeded: false,
                Details: "apply failed",
                RollbackCheckpointId: "checkpoint-failed"));
    }
    private sealed class RecordingApplyHandler(string applyVia, SelfEvolutionRisk maxRisk) : ISelfEvolutionApplyHandler
    {
        public List<string> Applied { get; } = [];
        public string ApplyVia { get; } = applyVia;
        public SelfEvolutionRisk MaxRisk { get; } = maxRisk;

        public Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)
        {
            Applied.Add(proposal.ProposalId);
            return Task.FromResult(new SelfEvolutionApplyResult(
                proposal.ProposalId,
                proposal.ApplyVia,
                Succeeded: true,
                Details: "applied",
                RollbackCheckpointId: "checkpoint-1"));
        }
    }
}


