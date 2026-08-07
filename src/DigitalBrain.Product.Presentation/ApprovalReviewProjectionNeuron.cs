using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Presentation;

public sealed class ApprovalReviewProjectionNeuron : Neuron<ApprovalReviewProjectionState>,
    INeuron<ApprovalPending>,
    INeuron<ApprovalStatusChanged>
{
    public const string Kind = "approval-review-projection";

    private static readonly IReadOnlyList<ApprovalReviewPlacement> ChatPlacementHints =
        Array.AsReadOnly([
            ApprovalReviewPlacement.Chat,
            ApprovalReviewPlacement.ContextDrawer,
            ApprovalReviewPlacement.Inbox,
        ]);

    private static readonly IReadOnlyList<ApprovalReviewPlacement> InboxPlacementHint =
        Array.AsReadOnly([ApprovalReviewPlacement.Inbox]);

    private static readonly IReadOnlyList<ApprovalReviewDecision> DecisionSlots =
        Array.AsReadOnly([
            ApprovalReviewDecision.Approve,
            ApprovalReviewDecision.Reject,
        ]);

    public Task HandleAsync(ApprovalPending synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMatchingApprovalOrigin(synapse.ProposalId))
        {
            return Task.CompletedTask;
        }

        var state = State;
        if (state.ProposalId is not null)
        {
            return Task.CompletedTask;
        }

        state.ProposalId = synapse.ProposalId;
        state.ProposalFingerprint = synapse.ProposalFingerprint;
        state.Title = synapse.Title;
        state.Summary = synapse.Summary;
        state.Status = ApprovalInboxStatus.Pending;
        State = state;

        var placements = synapse.ReviewContext?.Kind == ApprovalReviewContextKind.ChatConversation
            ? ChatPlacementHints
            : InboxPlacementHint;

        Emit(new ApprovalReviewSurfaceRequested(
            synapse.ProposalId,
            synapse.ProposalFingerprint,
            synapse.Title,
            synapse.Summary,
            synapse.Evidence,
            synapse.Changes,
            synapse.ExpiresAt,
            synapse.ReviewContext,
            DecisionSlots,
            placements));
        Emit(new ApprovalInboxItemChanged(
            synapse.ProposalId,
            synapse.ProposalFingerprint,
            synapse.Title,
            synapse.Summary,
            ApprovalInboxStatus.Pending));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ApprovalStatusChanged synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsMatchingApprovalOrigin(synapse.ProposalId))
        {
            return Task.CompletedTask;
        }

        var state = State;
        var proposalId = state.ProposalId;
        var proposalFingerprint = state.ProposalFingerprint;
        var title = state.Title;
        var summary = state.Summary;
        if (state.Status != ApprovalInboxStatus.Pending
            || proposalId is null
            || proposalFingerprint is null
            || title is null
            || summary is null
            || !string.Equals(proposalId, synapse.ProposalId, StringComparison.Ordinal)
            || !string.Equals(proposalFingerprint, synapse.ProposalFingerprint, StringComparison.Ordinal)
            || !IsFinal(synapse.Status))
        {
            return Task.CompletedTask;
        }

        state.Status = ApprovalInboxStatus.Resolved;
        State = state;
        Emit(new ApprovalInboxItemChanged(
            proposalId,
            proposalFingerprint,
            title,
            summary,
            ApprovalInboxStatus.Resolved));
        return Task.CompletedTask;
    }

    private bool IsMatchingApprovalOrigin(string proposalId)
        => string.Equals(Id.Name, proposalId, StringComparison.Ordinal)
            && Origin.Source == new NeuronId(ApprovalNeuron.Kind, proposalId);

    private static bool IsFinal(ApprovalStatus status)
        => status is ApprovalStatus.Approved or ApprovalStatus.Rejected or ApprovalStatus.Expired;
}
