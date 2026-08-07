using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Testing;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.Approvals;

public sealed class WholeProposalApprovalTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(ApprovalProposed).Assembly)
            .RegisterIngress<ApprovalProposalSubmitted>()
            .RegisterIngress<ApprovalDecisionSubmitted>()
            .RegisterIngress<ApprovalDeadlineElapsed>()
            .RegisterNeuron<ApprovalNeuron>(ApprovalNeuron.Kind)
            .RegisterNeuron<ApprovalWorkspaceInboxNeuron>(ApprovalWorkspaceInboxNeuron.Kind)
            .RegisterNeuron<ApprovalProposalIngress>(ApprovalProposalIngress.Kind)
            .RegisterNeuron<ApprovalDecisionIngress>(ApprovalDecisionIngress.Kind)
            .RegisterNeuron<ApprovalDeadlineIngress>(ApprovalDeadlineIngress.Kind)
            .RegisterNeuron<ApprovalGrantProbe>("approval-grant-probe");

    [Fact]
    public async Task GrantsExactlyTheFrozenWholeProposalToItsDeclaredTarget()
    {
        const string proposalId = "proposal-1";
        var approval = new NeuronId("approval", proposalId);
        var target = new NeuronId("approval-grant-probe", proposalId);
        var proposal = Proposal(proposalId, target);

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);

        var pendingPage = await WaitForJournalAsync(
            approval,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalPending).FullName),
            "a frozen pending approval",
            Cancellation);
        var pending = pendingPage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalPending).FullName);
        Assert.Equal(proposal.Fingerprint, pending.Serialization.GetProperty("proposalFingerprint").GetString());
        Assert.Equal("gmail", pending.Serialization.GetProperty("evidence")[0].GetProperty("source").GetString());
        Assert.Equal("Description", pending.Serialization.GetProperty("changes")[0].GetProperty("field").GetString());
        Assert.DoesNotContain("action", pending.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("executionTarget", pending.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(pendingPage.Records, record => record.SynapseKind == typeof(ApprovalGranted).FullName);

        var forged = await Assert.ThrowsAsync<InvalidOperationException>(() => PublishAsync(
            proposalId,
            new ApprovalGranted(
                proposal,
                Guid.NewGuid(),
                "actor/mallory",
                Clock.UtcNow),
            Cancellation));
        Assert.Contains(nameof(ApprovalGranted), forged.Message, StringComparison.Ordinal);
        Assert.Empty((await ReadAsync(target, cancellationToken: Cancellation)).Records);

        var decision = new ApprovalDecisionSubmitted(
            proposalId,
            proposal.Fingerprint,
            Guid.NewGuid(),
            ApprovalDecision.Approve);
        await PublishAsync("actor/ada", decision, Cancellation);

        var grantedPage = await WaitForJournalAsync(
            approval,
            page => page.Records.Count(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalGranted).FullName) == 1,
            "one exact approval grant",
            Cancellation);
        var granted = grantedPage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalGranted).FullName);
        Assert.Equal([target], granted.DeliveryTargets);
        Assert.Equal(proposal.Fingerprint, granted.Serialization.GetProperty("proposal").GetProperty("fingerprint").GetString());
        Assert.Equal("salesforce-mutation-1", granted.Serialization.GetProperty("proposal").GetProperty("action").GetProperty("actionId").GetString());
        Assert.Equal("actor/ada", granted.Serialization.GetProperty("actor").GetString());
        Assert.Equal(Clock.UtcNow, granted.Serialization.GetProperty("decidedAt").GetDateTimeOffset());

        _ = await WaitForJournalAsync(
            target,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(ApprovalGranted).FullName),
            "the declared approval grant",
            Cancellation);

        await PublishAsync(
            "actor/ada",
            new ApprovalDecisionSubmitted(
                proposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var duplicatePage = await WaitForJournalAsync(
            approval,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalDecisionIgnored).FullName),
            "a duplicate-decision outcome",
            Cancellation);
        Assert.Equal(1, duplicatePage.Records.Count(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalGranted).FullName));
    }

    [Fact]
    public async Task RejectsStaleAndExpiredDecisionsWithoutGrantingTheProposal()
    {
        const string proposalId = "proposal-2";
        var approval = new NeuronId("approval", proposalId);
        var proposal = Proposal(proposalId, new NeuronId("approval-grant-probe", proposalId));

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await PublishAsync(
            "actor/ada",
            new ApprovalDecisionSubmitted(
                proposalId,
                "wrong-fingerprint",
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        _ = await WaitForJournalAsync(
            approval,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalDecisionIgnored).FullName),
            "a stale-decision outcome",
            Cancellation);

        await Clock.AdvanceAsync(proposal.ExpiresAt - Clock.UtcNow, Cancellation);
        await PublishAsync(
            "approval-deadline-scheduler",
            new ApprovalDeadlineElapsed(
                proposalId,
                proposal.Fingerprint),
            Cancellation);

        _ = await WaitForJournalAsync(
            approval,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalExpired).FullName),
            "an expiry outcome",
            Cancellation);

        await PublishAsync(
            "actor/ada",
            new ApprovalDecisionSubmitted(
                proposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var page = await WaitForJournalAsync(
            approval,
            observed => observed.Records.Count(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalDecisionIgnored).FullName) >= 2,
            "a late-decision outcome",
            Cancellation);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalGranted).FullName);
    }

    [Fact]
    public async Task RetainsTheFrozenProposalAcrossHostReactivationBeforeDecision()
    {
        const string proposalId = "proposal-reload";
        var approval = new NeuronId("approval", proposalId);
        var target = new NeuronId("approval-grant-probe", proposalId);
        var proposal = Proposal(proposalId, target);

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        _ = await WaitForJournalAsync(
            approval,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalPending).FullName),
            "the durable pending proposal",
            Cancellation);

        await DeactivateAsync([approval], Cancellation);

        await PublishAsync(
            "actor/ada",
            new ApprovalDecisionSubmitted(
                proposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var page = await WaitForJournalAsync(
            approval,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalGranted).FullName),
            "the approval grant after reload",
            Cancellation);
        var grant = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalGranted).FullName);
        Assert.Equal(proposal.Fingerprint, grant.Serialization.GetProperty("proposal").GetProperty("fingerprint").GetString());
        Assert.Equal("salesforce-mutation-1", grant.Serialization.GetProperty("proposal").GetProperty("action").GetProperty("actionId").GetString());
    }

    [Fact]
    public async Task BuffersAVerifiedDecisionUntilItsFrozenProposalArrivesFromAnotherOutbox()
    {
        const string proposalId = "proposal-decision-before-proposal";
        var approval = new NeuronId(ApprovalNeuron.Kind, proposalId);
        var proposalIngress = new NeuronId(ApprovalProposalIngress.Kind, proposalId);
        var target = new NeuronId("approval-grant-probe", proposalId);
        var proposal = Proposal(proposalId, target);
        var decisionId = Guid.NewGuid();
        var fault = FailNextJournalRecording(proposalIngress, stickyUntilDisarm: true);

        try
        {
            await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
            await fault.Consumed.WaitAsync(Cancellation);

            await PublishAsync(
                "actor/out-of-order-ada",
                new ApprovalDecisionSubmitted(
                    proposalId,
                    proposal.Fingerprint,
                    decisionId,
                    ApprovalDecision.Approve),
                Cancellation);

            var buffered = await WaitForJournalAsync(
                approval,
                page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                    && record.SynapseKind == typeof(ApprovalDecisionRequested).FullName),
                "the verified decision before proposal delivery",
                Cancellation);
            Assert.DoesNotContain(buffered.Records, record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalGranted).FullName);
        }
        finally
        {
            await fault.DisposeAsync();
        }

        await DrainAsync(proposalIngress, Cancellation);
        var page = await WaitForJournalAsync(
            approval,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalGranted).FullName),
            "the buffered decision applied to the later frozen proposal",
            Cancellation);
        var grant = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalGranted).FullName);
        Assert.Equal(decisionId, grant.Serialization.GetProperty("decisionId").GetGuid());
        Assert.Equal(proposal.Fingerprint, grant.Serialization.GetProperty("proposal").GetProperty("fingerprint").GetString());
        Assert.Equal([target], grant.DeliveryTargets);
    }

    [Fact]
    public async Task RejectsAnApprovalArrivingAfterExpiryUsingTheHostStampedIngressTime()
    {
        const string proposalId = "proposal-host-time";
        var approval = new NeuronId(ApprovalNeuron.Kind, proposalId);
        var target = new NeuronId("approval-grant-probe", proposalId);
        var proposal = Proposal(proposalId, target, Clock.UtcNow.AddMinutes(5));

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        await Clock.AdvanceAsync(TimeSpan.FromMinutes(6), Cancellation);
        await PublishAsync(
            "actor/ada",
            new ApprovalDecisionSubmitted(
                proposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Approve),
            Cancellation);

        var page = await WaitForJournalAsync(
            approval,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalExpired).FullName),
            "an expiry stamped by the host clock",
            Cancellation);
        var expired = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalExpired).FullName);
        Assert.Equal(Clock.UtcNow, expired.Serialization.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalGranted).FullName);
    }

    [Fact]
    public async Task APreExpiryDecisionDelayedBehindTheDeadlineCannotReopenTheFinalApproval()
    {
        const string proposalId = "proposal-delayed-decision";
        var approval = new NeuronId(ApprovalNeuron.Kind, proposalId);
        var decisionIngress = new NeuronId(ApprovalDecisionIngress.Kind, "actor/delayed-ada");
        var proposal = Proposal(
            proposalId,
            new NeuronId("approval-grant-probe", proposalId),
            Clock.UtcNow.AddMinutes(5));

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);
        _ = await WaitForJournalAsync(
            approval,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalPending).FullName),
            "the pending approval before delayed decision delivery",
            Cancellation);
        await DrainAsync(approval, Cancellation);

        var fault = FailNextJournalRecording(approval, stickyUntilDisarm: true);
        try
        {
            await PublishAsync(
                "actor/delayed-ada",
                new ApprovalDecisionSubmitted(
                    proposalId,
                    proposal.Fingerprint,
                    Guid.NewGuid(),
                    ApprovalDecision.Approve),
                Cancellation);
            _ = await WaitForJournalAsync(
                decisionIngress,
                page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                    && record.SynapseKind == typeof(ApprovalDecisionSubmitted).FullName)
                    && page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                        && record.SynapseKind == typeof(ApprovalDecisionRequested).FullName),
                "the durable delayed-decision source outbox",
                Cancellation);
            await fault.Consumed.WaitAsync(Cancellation);
            await DeactivateAsync([decisionIngress], Cancellation);
        }
        finally
        {
            await fault.DisposeAsync();
        }

        await Clock.AdvanceAsync(TimeSpan.FromMinutes(6), Cancellation);
        await PublishAsync(
            "approval-deadline-scheduler",
            new ApprovalDeadlineElapsed(proposalId, proposal.Fingerprint),
            Cancellation);

        _ = await WaitForJournalAsync(
            approval,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalExpired).FullName),
            "the monotonic final expiry",
            Cancellation);

        await DrainAsync(decisionIngress, Cancellation);
        var page = await WaitForJournalAsync(
            approval,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalDecisionIgnored).FullName),
            "the delayed decision rejection",
            Cancellation);
        Assert.Equal(
            1,
            page.Records.Count(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalExpired).FullName));
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalGranted).FullName);
    }

    [Fact]
    public void RejectsUnknownApprovalDecisionsBeforeTheyCanEnterTheDurableIngress()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApprovalDecisionSubmitted(
            "proposal-invalid-decision",
            "proposal-fingerprint",
            Guid.NewGuid(),
            (ApprovalDecision)999));
    }

    private static ApprovalProposal Proposal(
        string proposalId,
        NeuronId target,
        DateTimeOffset? expiresAt = null)
        => new(
            proposalId,
            "Enrich Acme account",
            "Update Acme's Salesforce description from Gmail and web evidence.",
            [
                new ApprovalEvidence("gmail", "Acme announced its Series B."),
                new ApprovalEvidence("web", "Acme press page confirms the funding round."),
            ],
            [new ApprovalChange("Description", "", "Acme raised a Series B.")],
            new ApprovalActionBinding(
                "salesforce.account-description",
                "salesforce-mutation-1",
                "prepared-mutation-fingerprint",
                target),
            expiresAt ?? new DateTimeOffset(2040, 1, 1, 1, 0, 0, TimeSpan.Zero));
}
