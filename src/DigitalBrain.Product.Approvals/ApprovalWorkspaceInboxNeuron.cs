namespace DigitalBrain.Product.Approvals;

public sealed class ApprovalWorkspaceInboxNeuron : Neuron<ApprovalWorkspaceInboxState>,
    INeuron<ApprovalWorkspaceInboxItemChanged>
{
    public const string Kind = "approval-workspace-inbox";
    public const string Name = "pending-approvals";

    public Task HandleAsync(ApprovalWorkspaceInboxItemChanged synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var incoming = synapse.Item;
        if (!string.Equals(Id.Name, Name, StringComparison.Ordinal)
            || Origin.Source != new NeuronId(ApprovalNeuron.Kind, incoming.ProposalId))
        {
            return Task.CompletedTask;
        }

        var state = State;
        var items = state.Items.ToList();
        var index = items.FindIndex(item => string.Equals(
            item.ProposalId,
            incoming.ProposalId,
            StringComparison.Ordinal));
        if (index < 0)
        {
            if (incoming.Status != ApprovalWorkspaceItemStatus.Pending)
            {
                return Task.CompletedTask;
            }

            items.Add(incoming.WithStatus(ApprovalWorkspaceItemStatus.Pending));
        }
        else
        {
            var current = items[index];
            if (!string.Equals(current.ProposalFingerprint, incoming.ProposalFingerprint, StringComparison.Ordinal)
                || current.Status == incoming.Status
                || !CanTransition(current.Status, incoming.Status))
            {
                return Task.CompletedTask;
            }

            items[index] = current.WithStatus(incoming.Status);
        }

        state.Revision++;
        state.Items = items;
        State = state;
        Emit(new ApprovalWorkspaceInboxSnapshot(state.Revision, state.Items));
        return Task.CompletedTask;
    }

    private static bool CanTransition(
        ApprovalWorkspaceItemStatus current,
        ApprovalWorkspaceItemStatus next)
        => current == ApprovalWorkspaceItemStatus.Pending
            ? next is ApprovalWorkspaceItemStatus.Approved
                or ApprovalWorkspaceItemStatus.Rejected
                or ApprovalWorkspaceItemStatus.Expired
            : current == ApprovalWorkspaceItemStatus.Approved
                && next == ApprovalWorkspaceItemStatus.MutationUncertain;
}
