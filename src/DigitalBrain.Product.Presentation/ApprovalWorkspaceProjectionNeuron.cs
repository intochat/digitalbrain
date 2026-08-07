using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Presentation;

public sealed class ApprovalWorkspaceProjectionNeuron : Neuron<ApprovalWorkspaceProjectionState>,
    INeuron<ApprovalWorkspaceInboxSnapshot>
{
    public const string Kind = "approval-workspace-projection";

    private static readonly NeuronId Inbox = new(
        ApprovalWorkspaceInboxNeuron.Kind,
        ApprovalWorkspaceInboxNeuron.Name);

    private static readonly IReadOnlyList<ApprovalReviewPlacement> ChatPlacements =
        Array.AsReadOnly([
            ApprovalReviewPlacement.Chat,
            ApprovalReviewPlacement.ContextDrawer,
            ApprovalReviewPlacement.Inbox,
        ]);

    private static readonly IReadOnlyList<ApprovalReviewPlacement> InboxPlacement =
        Array.AsReadOnly([ApprovalReviewPlacement.Inbox]);

    public Task HandleAsync(ApprovalWorkspaceInboxSnapshot synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var state = State;
        if (!string.Equals(Id.Name, ApprovalWorkspaceInboxNeuron.Name, StringComparison.Ordinal)
            || Origin.Source != Inbox
            || synapse.Revision <= state.Revision)
        {
            return Task.CompletedTask;
        }

        var items = synapse.Items.Select(Project).ToList();
        state.Revision = synapse.Revision;
        state.Items = items;
        State = state;
        Emit(new ApprovalWorkspaceSurfaceRequested(state.Revision, state.Items));
        return Task.CompletedTask;
    }

    private static ApprovalWorkspaceSurfaceItem Project(ApprovalWorkspaceInboxItem item)
    {
        var placements = item.Context?.Kind == ApprovalReviewContextKind.ChatConversation
            ? ChatPlacements
            : InboxPlacement;
        IReadOnlyList<ApprovalWorkspaceSurfaceAction> actions = Array.AsReadOnly([
            Action(item, ApprovalReviewDecision.Approve),
            Action(item, ApprovalReviewDecision.Reject),
        ]);
        return new ApprovalWorkspaceSurfaceItem(
            item.ProposalId,
            item.ProposalFingerprint,
            item.Title,
            item.Summary,
            item.Evidence,
            item.Changes,
            item.ExpiresAt,
            item.Context,
            item.Status,
            placements,
            actions);
    }

    private static ApprovalWorkspaceSurfaceAction Action(
        ApprovalWorkspaceInboxItem item,
        ApprovalReviewDecision decision)
    {
        var material = string.Join(
            '\0',
            "approval-workspace-action-v1",
            item.ProposalId,
            item.ProposalFingerprint,
            ((int)decision).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return new ApprovalWorkspaceSurfaceAction(
            decision,
            "apr_" + Convert.ToHexString(digest));
    }
}
