using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Presentation;

namespace DigitalBrain.Edge;

/// <summary>
/// Resolves an opaque approval action from the current presentation snapshot,
/// authorizes it, and publishes only the existing approval decision ingress.
/// </summary>
public sealed class ApprovalUiActionBridge : IApprovalUiActionBridge
{
    private readonly WorkspaceUiSurfaceSource source;
    private readonly SynapsePublisher publisher;
    private readonly IUiActionAuthorizer authorizer;

    public ApprovalUiActionBridge(WorkspaceChannel channel, IUiActionAuthorizer authorizer)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(authorizer);
        var channelPublisher = channel.Publisher;
        ArgumentNullException.ThrowIfNull(channelPublisher);

        source = new WorkspaceUiSurfaceSource(channel);
        publisher = channelPublisher;
        this.authorizer = authorizer;
    }

    public async Task<UiActionReceipt> InvokeAsync(
        OpaqueUiActionReference action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.Value))
        {
            return new UiActionReceipt(false);
        }

        var approval = await source.ReadApprovalsAsync(cancellationToken).ConfigureAwait(false);
        var matched = FindPendingAction(approval, action);
        if (matched is null)
        {
            return new UiActionReceipt(false);
        }

        if (!await authorizer.AuthorizeAsync(action, cancellationToken).ConfigureAwait(false))
        {
            return new UiActionReceipt(false);
        }

        var decision = matched.Value.Action.Decision switch
        {
            ApprovalReviewDecision.Approve => ApprovalDecision.Approve,
            ApprovalReviewDecision.Reject => ApprovalDecision.Reject,
            _ => throw new InvalidOperationException("The approval action is not recognized."),
        };
        await publisher.PublishAsync(
            new ApprovalDecisionSubmitted(
                matched.Value.Item.ProposalId,
                matched.Value.Item.ProposalFingerprint,
                DecisionId(action),
                decision),
            cancellationToken).ConfigureAwait(false);
        return new UiActionReceipt(true);
    }

    private static (ApprovalWorkspaceSurfaceItem Item, ApprovalWorkspaceSurfaceAction Action)? FindPendingAction(
        ApprovalWorkspaceSurfaceRequested? approval,
        OpaqueUiActionReference action)
    {
        if (approval is null)
        {
            return null;
        }

        foreach (var item in approval.Items)
        {
            if (item.Status != ApprovalWorkspaceItemStatus.Pending)
            {
                continue;
            }

            foreach (var candidate in item.Actions)
            {
                if (string.Equals(candidate.Reference, action.Value, StringComparison.Ordinal))
                {
                    return (item, candidate);
                }
            }
        }

        return null;
    }

    private static Guid DecisionId(OpaqueUiActionReference action)
    {
        var material = string.Join('\0', "approval-ui-decision-v1", action.Value);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Guid.ParseExact(Convert.ToHexString(digest.AsSpan(0, 16)), "N");
    }
}
