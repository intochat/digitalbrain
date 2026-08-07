using System.Collections.Concurrent;
using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Testing;
using DigitalBrain.Product.Time;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.Time;

public sealed class ProposalDeadlineTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(ApprovalProposed).Assembly)
            .RegisterVocabulary(typeof(ProposalDeadlineArmed).Assembly)
            .RegisterVocabulary(typeof(ForgeApprovalPending).Assembly)
            .RegisterIngress<ApprovalProposalSubmitted>()
            .RegisterIngress<ForgeApprovalPending>()
            .RegisterWorkspaceService<IProposalDeadlineScheduler>(static _ => new RecordingDeadlineScheduler())
            .RegisterNeuron<ApprovalNeuron>(ApprovalNeuron.Kind)
            .RegisterNeuron<ApprovalProposalIngress>(ApprovalProposalIngress.Kind)
            .RegisterNeuron<ForgedApprovalPendingEmitter>(ForgedApprovalPendingEmitter.Kind)
            .RegisterNeuron<ProposalDeadlineNeuron>("proposal-deadline");

    [Fact]
    public async Task SchedulesTheExactFrozenPendingProposalDeadlineOnce()
    {
        RecordingDeadlineScheduler.Reset();
        const string proposalId = "proposal-deadline";
        var deadlineNeuron = new NeuronId("proposal-deadline", proposalId);
        var proposal = new ApprovalProposal(
            proposalId,
            "Enrich Acme account",
            "Update Acme after evidence review.",
            [new ApprovalEvidence("gmail", "Acme announced funding.")],
            [new ApprovalChange("Description", "", "Acme raised funding.")],
            new ApprovalActionBinding(
                "salesforce.account-description",
                "salesforce-mutation-deadline",
                "prepared-mutation-fingerprint",
                new NeuronId("approval", proposalId)),
            new DateTimeOffset(2040, 1, 2, 0, 0, 0, TimeSpan.Zero));

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);

        _ = await WaitForJournalAsync(
            deadlineNeuron,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ProposalDeadlineArmed).FullName),
            "an armed proposal deadline",
            Cancellation);

        var scheduled = RecordingDeadlineScheduler.Scheduled.ToArray();
        var deadline = Assert.Single(scheduled);
        Assert.Equal(proposalId, deadline.ProposalId);
        Assert.Equal(proposal.Fingerprint, deadline.ProposalFingerprint);
        Assert.Equal(proposal.ExpiresAt, deadline.DueAt);
    }

    [Fact]
    public async Task DoesNotScheduleAPendingFactNotProducedByTheMatchingApprovalNeuron()
    {
        RecordingDeadlineScheduler.Reset();
        const string proposalId = "proposal-forged-pending";
        var deadlineNeuron = new NeuronId(ProposalDeadlineNeuron.Kind, proposalId);
        var proposal = new ApprovalProposal(
            proposalId,
            "Enrich Acme account",
            "Update Acme after evidence review.",
            [new ApprovalEvidence("gmail", "Acme announced funding.")],
            [new ApprovalChange("Description", "", "Acme raised funding.")],
            new ApprovalActionBinding(
                "salesforce.account-description",
                "salesforce-mutation-forged-pending",
                "prepared-mutation-fingerprint",
                new NeuronId("approval", proposalId)),
            new DateTimeOffset(2040, 1, 2, 0, 0, 0, TimeSpan.Zero));

        await PublishAsync(proposalId, new ForgeApprovalPending(proposal), Cancellation);

        var page = await WaitForJournalAsync(
            deadlineNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(ApprovalPending).FullName),
            "the forged pending fact",
            Cancellation);

        Assert.Empty(RecordingDeadlineScheduler.Scheduled);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ProposalDeadlineArmed).FullName);
    }

    [Fact]
    public async Task RecordingFailureReplayConvergesOnOneLogicalDeadline()
    {
        RecordingDeadlineScheduler.Reset();
        const string proposalId = "proposal-deadline-replay";
        var deadlineNeuron = new NeuronId(ProposalDeadlineNeuron.Kind, proposalId);
        var approvalNeuron = new NeuronId(ApprovalNeuron.Kind, proposalId);
        var proposal = new ApprovalProposal(
            proposalId,
            "Enrich Acme account",
            "Update Acme after evidence review.",
            [new ApprovalEvidence("gmail", "Acme announced funding.")],
            [new ApprovalChange("Description", "", "Acme raised funding.")],
            new ApprovalActionBinding(
                "salesforce.account-description",
                "salesforce-mutation-replay",
                "prepared-mutation-fingerprint",
                new NeuronId("approval", proposalId)),
            new DateTimeOffset(2040, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var fault = FailNextJournalRecording(deadlineNeuron);

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await fault.Consumed.WaitAsync(Cancellation);
        await DeactivateAsync([deadlineNeuron], Cancellation);
        await DrainAsync(approvalNeuron, Cancellation);

        _ = await WaitForJournalAsync(
            deadlineNeuron,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ProposalDeadlineArmed).FullName),
            "the replayed deadline arm",
            Cancellation);

        var deadline = Assert.Single(RecordingDeadlineScheduler.Scheduled);
        Assert.Equal(proposalId, deadline.ProposalId);
        Assert.Equal(proposal.Fingerprint, deadline.ProposalFingerprint);
        Assert.Equal(proposal.ExpiresAt, deadline.DueAt);
    }

    private sealed class RecordingDeadlineScheduler : IProposalDeadlineScheduler
    {
        private static readonly ConcurrentDictionary<DeadlineIdentity, ProposalDeadline> scheduled = [];

        internal static IReadOnlyCollection<ProposalDeadline> Scheduled => [.. scheduled.Values];

        internal static void Reset()
            => scheduled.Clear();

        public Task ScheduleAsync(ProposalDeadline deadline, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(deadline);
            cancellationToken.ThrowIfCancellationRequested();

            var identity = new DeadlineIdentity(deadline.ProposalId, deadline.ProposalFingerprint);
            while (true)
            {
                if (scheduled.TryGetValue(identity, out var existing))
                {
                    if (existing.DueAt != deadline.DueAt)
                    {
                        throw new InvalidOperationException(
                            "A proposal deadline identity cannot be scheduled at two different times.");
                    }

                    return Task.CompletedTask;
                }

                if (scheduled.TryAdd(identity, deadline))
                {
                    return Task.CompletedTask;
                }
            }
        }

        private readonly record struct DeadlineIdentity(string ProposalId, string ProposalFingerprint);
    }
}
