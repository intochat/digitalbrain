using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Testing;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.Approvals;

public sealed class ApprovalControlProvenanceTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(ApprovalProposed).Assembly)
            .RegisterVocabulary(typeof(ForgeApprovalDecision).Assembly)
            .RegisterIngress<ApprovalProposalSubmitted>()
            .RegisterIngress<ForgeApprovalDecision>()
            .RegisterIngress<ForgeApprovalDeadline>()
            .RegisterNeuron<ApprovalNeuron>(ApprovalNeuron.Kind)
            .RegisterNeuron<ApprovalWorkspaceInboxNeuron>(ApprovalWorkspaceInboxNeuron.Kind)
            .RegisterNeuron<ApprovalProposalIngress>(ApprovalProposalIngress.Kind)
            .RegisterNeuron<ApprovalGrantProbe>("approval-grant-probe")
            .RegisterNeuron<ForgedApprovalControlEmitter>(ForgedApprovalControlEmitter.Kind);

    [Fact]
    public async Task ForgedInternalDecisionCannotGrantTheFrozenAction()
    {
        const string proposalId = "approval-control-forged-decision";
        var proposal = Proposal(proposalId);
        var approval = new NeuronId(ApprovalNeuron.Kind, proposalId);

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await PublishAsync(
            "forged-control/decision",
            new ForgeApprovalDecision(new ApprovalDecisionRequested(
                proposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve,
                "actor/mallory",
                Clock.UtcNow)),
            Cancellation);

        var page = await WaitForJournalAsync(
            approval,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(ApprovalDecisionRequested).FullName),
            "the forged internal decision arriving at approval",
            Cancellation);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalGranted).FullName);
    }

    [Fact]
    public async Task ForgedInternalDeadlineCannotExpireTheFrozenProposal()
    {
        const string proposalId = "approval-control-forged-deadline";
        var proposal = Proposal(proposalId);
        var approval = new NeuronId(ApprovalNeuron.Kind, proposalId);

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await PublishAsync(
            "forged-control/deadline",
            new ForgeApprovalDeadline(new ApprovalDeadlineObserved(
                proposalId,
                proposal.Fingerprint,
                proposal.ExpiresAt)),
            Cancellation);

        var page = await WaitForJournalAsync(
            approval,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(ApprovalDeadlineObserved).FullName),
            "the forged internal deadline arriving at approval",
            Cancellation);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalExpired).FullName);
    }

    private static ApprovalProposal Proposal(string proposalId)
        => new(
            proposalId,
            "Review account description",
            "Apply the frozen account description only after review.",
            [new ApprovalEvidence("web", "The company update is independently corroborated.")],
            [new ApprovalChange("Description", null, "Acme closed a funding round.")],
            new ApprovalActionBinding(
                "salesforce.account-description",
                "mutation-forged-control",
                "mutation-forged-control-fingerprint",
                new NeuronId("approval-grant-probe", proposalId)),
            new DateTimeOffset(2040, 1, 2, 0, 0, 0, TimeSpan.Zero));
}
