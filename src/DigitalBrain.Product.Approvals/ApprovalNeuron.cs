namespace DigitalBrain.Product.Approvals;

public sealed class ApprovalNeuron : Neuron<ApprovalState>,
    INeuron<ApprovalProposed>,
    INeuron<ApprovalDecisionRequested>,
    INeuron<ApprovalDeadlineObserved>,
    INeuron<ApprovalMutationOutcomeUncertain>
{
    public const string Kind = "approval";

    public Task HandleAsync(ApprovalProposed synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesId(synapse.Proposal.ProposalId))
        {
            Ignore(synapse.Proposal.ProposalId, null, ApprovalDecisionIgnoreReason.ProposalIdentityMismatch);
            return Task.CompletedTask;
        }

        var state = State;
        if (state.Proposal is not null)
        {
            Ignore(synapse.Proposal.ProposalId, null, ApprovalDecisionIgnoreReason.ProposalAlreadyRecorded);
            return Task.CompletedTask;
        }

        var pending = new ApprovalPending(synapse.Proposal);
        state.Proposal = synapse.Proposal;
        state.Status = ApprovalStatus.Pending;
        state.WorkspaceItem = ApprovalWorkspaceInboxItem.Pending(pending);
        var bufferedDecision = state.BufferedDecision;
        state.BufferedDecision = null;
        State = state;
        Emit(pending);
        EmitWorkspaceItemChanged(state);
        if (bufferedDecision is not null)
        {
            ProcessDecision(state, bufferedDecision);
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(ApprovalDecisionRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesId(synapse.ProposalId))
        {
            Ignore(synapse.ProposalId, synapse.DecisionId, ApprovalDecisionIgnoreReason.ProposalIdentityMismatch);
            return Task.CompletedTask;
        }

        if (!IsDecisionIngressOrigin(synapse))
        {
            Ignore(synapse.ProposalId, synapse.DecisionId, ApprovalDecisionIgnoreReason.UntrustedControlOrigin);
            return Task.CompletedTask;
        }

        var state = State;
        if (state.Proposal is null)
        {
            if (state.BufferedDecision is null)
            {
                state.BufferedDecision = synapse;
                State = state;
            }
            else
            {
                Ignore(synapse.ProposalId, synapse.DecisionId, ApprovalDecisionIgnoreReason.ProposalMissing);
            }

            return Task.CompletedTask;
        }

        ProcessDecision(state, synapse);
        return Task.CompletedTask;
    }

    private void ProcessDecision(ApprovalState state, ApprovalDecisionRequested synapse)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(synapse);

        var proposal = state.Proposal
            ?? throw new InvalidOperationException("A buffered approval decision needs a frozen proposal.");
        if (!string.Equals(proposal.Fingerprint, synapse.ExpectedProposalFingerprint, StringComparison.Ordinal))
        {
            Ignore(synapse.ProposalId, synapse.DecisionId, ApprovalDecisionIgnoreReason.FingerprintMismatch);
            return;
        }

        if (state.Status == ApprovalStatus.Expired)
        {
            Ignore(synapse.ProposalId, synapse.DecisionId, ApprovalDecisionIgnoreReason.Expired);
            return;
        }

        if (state.Status != ApprovalStatus.Pending)
        {
            Ignore(synapse.ProposalId, synapse.DecisionId, ApprovalDecisionIgnoreReason.AlreadyFinalized);
            return;
        }

        if (synapse.DecidedAt >= proposal.ExpiresAt)
        {
            state.Status = ApprovalStatus.Expired;
            SetWorkspaceStatus(state, ApprovalWorkspaceItemStatus.Expired);
            State = state;
            Emit(new ApprovalExpired(proposal, synapse.DecidedAt));
            EmitStatusChanged(proposal, ApprovalStatus.Expired);
            EmitWorkspaceItemChanged(state);
            Ignore(synapse.ProposalId, synapse.DecisionId, ApprovalDecisionIgnoreReason.Expired);
            return;
        }

        switch (synapse.Decision)
        {
            case ApprovalDecision.Approve:
                RecordDecision(state, synapse);
                state.Status = ApprovalStatus.Approved;
                SetWorkspaceStatus(state, ApprovalWorkspaceItemStatus.Approved);
                State = state;
                Emit(
                    new ApprovalGranted(proposal, synapse.DecisionId, synapse.Actor, synapse.DecidedAt),
                    Dispatch.Direct(proposal.Action.ExecutionTarget));
                EmitStatusChanged(proposal, ApprovalStatus.Approved);
                EmitWorkspaceItemChanged(state);
                return;
            case ApprovalDecision.Reject:
                RecordDecision(state, synapse);
                state.Status = ApprovalStatus.Rejected;
                SetWorkspaceStatus(state, ApprovalWorkspaceItemStatus.Rejected);
                State = state;
                Emit(new ApprovalRejected(proposal, synapse.DecisionId, synapse.Actor, synapse.DecidedAt));
                EmitStatusChanged(proposal, ApprovalStatus.Rejected);
                EmitWorkspaceItemChanged(state);
                return;
            default:
                Ignore(synapse.ProposalId, synapse.DecisionId, ApprovalDecisionIgnoreReason.InvalidDecision);
                return;
        }
    }

    public Task HandleAsync(ApprovalDeadlineObserved synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesId(synapse.ProposalId))
        {
            Ignore(synapse.ProposalId, null, ApprovalDecisionIgnoreReason.ProposalIdentityMismatch);
            return Task.CompletedTask;
        }

        if (!IsDeadlineIngressOrigin())
        {
            Ignore(synapse.ProposalId, null, ApprovalDecisionIgnoreReason.UntrustedControlOrigin);
            return Task.CompletedTask;
        }

        var state = State;
        if (state.Proposal is not { } proposal)
        {
            Ignore(synapse.ProposalId, null, ApprovalDecisionIgnoreReason.ProposalMissing);
            return Task.CompletedTask;
        }

        if (!string.Equals(proposal.Fingerprint, synapse.ExpectedProposalFingerprint, StringComparison.Ordinal))
        {
            Ignore(synapse.ProposalId, null, ApprovalDecisionIgnoreReason.FingerprintMismatch);
            return Task.CompletedTask;
        }

        if (state.Status != ApprovalStatus.Pending)
        {
            Ignore(synapse.ProposalId, null, ApprovalDecisionIgnoreReason.AlreadyFinalized);
            return Task.CompletedTask;
        }

        if (synapse.OccurredAt < proposal.ExpiresAt)
        {
            Ignore(synapse.ProposalId, null, ApprovalDecisionIgnoreReason.DeadlineNotReached);
            return Task.CompletedTask;
        }

        state.Status = ApprovalStatus.Expired;
        SetWorkspaceStatus(state, ApprovalWorkspaceItemStatus.Expired);
        State = state;
        Emit(new ApprovalExpired(proposal, synapse.OccurredAt));
        EmitStatusChanged(proposal, ApprovalStatus.Expired);
        EmitWorkspaceItemChanged(state);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ApprovalMutationOutcomeUncertain synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var state = State;
        if (!MatchesId(synapse.ProposalId)
            || state.MutationOutcomeUncertain
            || state.Status != ApprovalStatus.Approved
            || state.Proposal is not { } proposal
            || !string.Equals(proposal.Fingerprint, synapse.ProposalFingerprint, StringComparison.Ordinal)
            || Origin.Source != proposal.Action.ExecutionTarget)
        {
            return Task.CompletedTask;
        }

        state.MutationOutcomeUncertain = true;
        SetWorkspaceStatus(state, ApprovalWorkspaceItemStatus.MutationUncertain);
        State = state;
        EmitWorkspaceItemChanged(state);
        return Task.CompletedTask;
    }

    private bool MatchesId(string proposalId)
        => string.Equals(Id.Name, proposalId, StringComparison.Ordinal);

    private bool IsDecisionIngressOrigin(ApprovalDecisionRequested synapse)
        => string.Equals(Origin.Source.Kind, ApprovalDecisionIngress.Kind, StringComparison.Ordinal)
            && string.Equals(synapse.Actor, Origin.Source.Name, StringComparison.Ordinal);

    private bool IsDeadlineIngressOrigin()
        => string.Equals(Origin.Source.Kind, ApprovalDeadlineIngress.Kind, StringComparison.Ordinal);

    private static void RecordDecision(ApprovalState state, ApprovalDecisionRequested synapse)
    {
        state.DecisionId = synapse.DecisionId;
        state.Actor = synapse.Actor;
        state.DecidedAt = synapse.DecidedAt;
    }

    private void Ignore(string proposalId, Guid? decisionId, ApprovalDecisionIgnoreReason reason)
        => Emit(new ApprovalDecisionIgnored(proposalId, decisionId, reason));

    private void EmitStatusChanged(ApprovalProposal proposal, ApprovalStatus status)
        => Emit(new ApprovalStatusChanged(
            proposal.ProposalId,
            proposal.Fingerprint,
            status));

    private static void SetWorkspaceStatus(ApprovalState state, ApprovalWorkspaceItemStatus status)
    {
        var workspaceItem = state.WorkspaceItem
            ?? throw new InvalidOperationException("An approval lifecycle transition needs its frozen workspace item.");
        state.WorkspaceItem = workspaceItem.WithStatus(status);
    }

    private void EmitWorkspaceItemChanged(ApprovalState state)
    {
        var workspaceItem = state.WorkspaceItem
            ?? throw new InvalidOperationException("An approval workspace update needs its frozen review item.");
        Emit(
            new ApprovalWorkspaceInboxItemChanged(workspaceItem),
            Dispatch.Direct(new NeuronId(
                ApprovalWorkspaceInboxNeuron.Kind,
                ApprovalWorkspaceInboxNeuron.Name)));
    }
}
