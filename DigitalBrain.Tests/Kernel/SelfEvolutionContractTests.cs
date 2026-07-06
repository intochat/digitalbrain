using DigitalBrain.Core;

namespace DigitalBrain.Tests.Kernel;

// Pins the self-evolution rail vocabulary. SelfEvolutionProposal/SelfEvolutionDecision are
// journaled wire contracts consumed by SoftwareEngineeringClosedLoopNeuron's staging flow
// (see docs/architecture-trash-analysis-2026-07-06.md §0 — these types were once deleted as
// "unused" while still referenced, breaking master). This test keeps the rail load-bearing.
public class SelfEvolutionContractTests
{
    [Fact]
    public void Proposal_carries_approval_and_rollback_contract()
    {
        var proposal = new SelfEvolutionProposal(
            ProposalId: "p-1",
            Scope: "kernel",
            Rationale: "why",
            ProposedChange: "what",
            ApplyVia: "aspire-mcp",
            Risk: SelfEvolutionRisk.KernelRestart,
            RequiresHumanApproval: true,
            RollbackPlan: "checkpoint-then-rolling-rollback",
            Origin: "closedloop-neuron");

        Assert.Equal(nameof(SelfEvolutionProposal), proposal.Type);
        Assert.True(proposal.RequiresHumanApproval);
        Assert.Null(proposal.ExpiresAt); // expiry is opt-in at staging time, never implicit
        Assert.False(string.IsNullOrWhiteSpace(proposal.RollbackPlan));
        Assert.Equal(SelfEvolutionRisk.KernelRestart, proposal.Risk);
    }

    [Fact]
    public void Decision_records_who_consented_to_which_proposal()
    {
        var decision = new SelfEvolutionDecision("p-1", Approved: true, DecidedBy: "user:owner");

        Assert.Equal(nameof(SelfEvolutionDecision), decision.Type);
        Assert.Equal("p-1", decision.ProposalId);
        Assert.True(decision.Approved);
        Assert.Equal("user:owner", decision.DecidedBy);
        Assert.Equal(string.Empty, decision.Reason); // reason optional, never null
    }
}
