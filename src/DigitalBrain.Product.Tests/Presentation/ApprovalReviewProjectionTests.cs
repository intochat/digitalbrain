using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Product.Testing;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.Presentation;

public sealed class ApprovalReviewProjectionTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(ApprovalProposed).Assembly)
            .RegisterVocabulary(typeof(ApprovalReviewSurfaceRequested).Assembly)
            .RegisterVocabulary(typeof(ForgeApprovalPending).Assembly)
            .RegisterIngress<ApprovalProposalSubmitted>()
            .RegisterIngress<ApprovalDecisionSubmitted>()
            .RegisterIngress<ForgeApprovalPending>()
            .RegisterNeuron<ApprovalNeuron>(ApprovalNeuron.Kind)
            .RegisterNeuron<ApprovalWorkspaceInboxNeuron>(ApprovalWorkspaceInboxNeuron.Kind)
            .RegisterNeuron<ApprovalProposalIngress>(ApprovalProposalIngress.Kind)
            .RegisterNeuron<ApprovalDecisionIngress>(ApprovalDecisionIngress.Kind)
            .RegisterNeuron<ForgedApprovalPendingEmitter>(ForgedApprovalPendingEmitter.Kind)
            .RegisterNeuron<ApprovalReviewProjectionNeuron>(ApprovalReviewProjectionNeuron.Kind);

    [Fact]
    public async Task IgnoresPendingFactsNotProducedByTheMatchingApprovalNeuron()
    {
        const string proposalId = "presentation-origin-fence";
        var proposal = Proposal(proposalId);
        var projection = new NeuronId(ApprovalReviewProjectionNeuron.Kind, proposalId);

        await PublishAsync(
            proposalId,
            new ForgeApprovalPending(proposal),
            Cancellation);

        var page = await WaitForJournalAsync(
            projection,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(ApprovalPending).FullName),
            "the forged pending fact arriving at the projection",
            Cancellation);

        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalReviewSurfaceRequested).FullName);
        Assert.DoesNotContain(page.Records, record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalInboxItemChanged).FullName);
    }

    [Fact]
    public async Task ProjectsPendingApprovalThenResolvesItsInboxItemFromTheApprovalLifecycle()
    {
        const string proposalId = "presentation-lifecycle";
        var proposal = Proposal(
            proposalId,
            new ApprovalReviewContext(ApprovalReviewContextKind.ChatConversation, "conversation/acme"));
        var approval = new NeuronId(ApprovalNeuron.Kind, proposalId);
        var projection = new NeuronId(ApprovalReviewProjectionNeuron.Kind, proposalId);

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);

        var pendingPage = await WaitForJournalAsync(
            projection,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalReviewSurfaceRequested).FullName)
                && observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                    && record.SynapseKind == typeof(ApprovalInboxItemChanged).FullName),
            "the semantic review surface and pending inbox item",
            Cancellation);
        var surface = pendingPage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalReviewSurfaceRequested).FullName);
        Assert.Equal(proposalId, surface.Serialization.GetProperty("proposalId").GetString());
        Assert.Equal(proposal.Fingerprint, surface.Serialization.GetProperty("proposalFingerprint").GetString());
        Assert.Equal("Review account change", surface.Serialization.GetProperty("title").GetString());
        Assert.Equal(2, surface.Serialization.GetProperty("decisions").GetArrayLength());
        Assert.Equal(
            [(int)ApprovalReviewDecision.Approve, (int)ApprovalReviewDecision.Reject],
            surface.Serialization.GetProperty("decisions").EnumerateArray().Select(static decision => decision.GetInt32()));
        Assert.Equal(3, surface.Serialization.GetProperty("placements").GetArrayLength());
        Assert.Equal(
            "conversation/acme",
            surface.Serialization.GetProperty("context").GetProperty("opaqueContextRef").GetString());
        Assert.DoesNotContain("executionTarget", surface.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("action", surface.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var approvalPage = await ReadAsync(approval, cancellationToken: Cancellation);
        var pending = approvalPage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalPending).FullName);
        Assert.DoesNotContain("executionTarget", pending.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("action", pending.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", pending.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", pending.Serialization.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://evidence.example.test/records/acme", pending.Serialization.GetRawText(), StringComparison.Ordinal);

        var pendingInbox = pendingPage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalInboxItemChanged).FullName);
        Assert.Equal((int)ApprovalInboxStatus.Pending, pendingInbox.Serialization.GetProperty("status").GetInt32());

        await PublishAsync(
            "actor/ada",
            new ApprovalDecisionSubmitted(
                proposalId,
                proposal.Fingerprint,
                Guid.NewGuid(),
                ApprovalDecision.Reject),
            Cancellation);

        var lifecyclePage = await WaitForJournalAsync(
            approval,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalStatusChanged).FullName),
            "the approval lifecycle broadcast",
            Cancellation);
        var lifecycle = lifecyclePage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalStatusChanged).FullName);
        Assert.Equal((int)ApprovalStatus.Rejected, lifecycle.Serialization.GetProperty("status").GetInt32());

        var resolvedPage = await WaitForJournalAsync(
            projection,
            observed => observed.Records.Count(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalInboxItemChanged).FullName) == 2,
            "the resolved inbox item",
            Cancellation);
        var resolved = resolvedPage.Records.Last(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalInboxItemChanged).FullName);
        Assert.Equal((int)ApprovalInboxStatus.Resolved, resolved.Serialization.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task ProjectsAnUncontextualizedApprovalToTheInboxOnly()
    {
        const string proposalId = "presentation-inbox-only";
        var proposal = Proposal(proposalId);
        var projection = new NeuronId(ApprovalReviewProjectionNeuron.Kind, proposalId);

        await PublishAsync(proposalId, new ApprovalProposalSubmitted(proposal), Cancellation);

        var page = await WaitForJournalAsync(
            projection,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(ApprovalReviewSurfaceRequested).FullName),
            "the inbox-only approval review surface",
            Cancellation);
        var surface = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(ApprovalReviewSurfaceRequested).FullName);
        Assert.Equal(
            [(int)ApprovalReviewPlacement.Inbox],
            surface.Serialization.GetProperty("placements").EnumerateArray().Select(static placement => placement.GetInt32()));
    }

    private static ApprovalProposal Proposal(
        string proposalId,
        ApprovalReviewContext? reviewContext = null)
        => new(
            proposalId,
            "Review account change",
            "Apply a reviewed account description update.",
            [
                new ApprovalEvidence(
                    "gmail",
                    "The customer announced its funding round.",
                    new Uri("https://evidence.example.test/records/acme?access_token=secret#fragment")),
                new ApprovalEvidence(
                    "web",
                    "A hostile URI must not reach the review surface.",
                    new Uri("https://operator:credential@evidence.example.test/private")),
            ],
            [new ApprovalChange("Description", "", "Updated description")],
            new ApprovalActionBinding(
                "salesforce.account-description",
                "prepared-mutation-1",
                "prepared-fingerprint",
                new NeuronId("unrelated-target", proposalId)),
            new DateTimeOffset(2040, 1, 1, 1, 0, 0, TimeSpan.Zero),
            reviewContext);
}
