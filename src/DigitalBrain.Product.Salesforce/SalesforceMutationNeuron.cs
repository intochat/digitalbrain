using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Salesforce;

public sealed class SalesforceMutationNeuron : Neuron<SalesforceMutationState>,
    INeuron<PreparedSalesforceMutation>,
    INeuron<ApprovalGranted>,
    INeuron<SalesforceChangeConfirmed>,
    INeuron<SalesforceChangeOutcomeUncertain>
{
    public const string Kind = "salesforce-mutation";

    public Task HandleAsync(PreparedSalesforceMutation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesId(synapse.Mutation) || State.Mutation is not null)
        {
            return Task.CompletedTask;
        }

        var state = State;
        state.Mutation = synapse.Mutation;
        State = state;
        Emit(new SalesforceMutationPrepared(synapse.Mutation));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ApprovalGranted synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var state = State;
        if (state.Mutation is not { } mutation
            || state.InvocationRequested
            || !Equals(Origin.Source, new NeuronId(ApprovalNeuron.Kind, synapse.Proposal.ProposalId))
            || !Equals(synapse.Proposal.Action.ExecutionTarget, Id)
            || !MatchesApprovalBinding(synapse, mutation))
        {
            return Task.CompletedTask;
        }

        state.InvocationRequested = true;
        state.ApprovedProposalId = synapse.Proposal.ProposalId;
        state.ApprovedProposalFingerprint = synapse.Proposal.Fingerprint;
        State = state;
        Emit(
            new SalesforceInvocationRequested(mutation),
            Dispatch.Direct(new NeuronId(SalesforceEffectNeuron.Kind, mutation.MutationId)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesforceChangeConfirmed synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RecordOutcome(synapse.Mutation, SalesforceGatewayOutcome.Confirmed, synapse);
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesforceChangeOutcomeUncertain synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RecordOutcome(synapse.Mutation, SalesforceGatewayOutcome.OutcomeUncertain, synapse);
        return Task.CompletedTask;
    }

    private bool MatchesId(PreparedAccountDescriptionMutation mutation)
        => string.Equals(Id.Name, mutation.MutationId, StringComparison.Ordinal);

    private static bool MatchesApprovalBinding(
        ApprovalGranted grant,
        PreparedAccountDescriptionMutation mutation)
        => string.Equals(grant.Proposal.Action.ActionKind, PreparedAccountDescriptionMutation.ActionKind, StringComparison.Ordinal)
            && string.Equals(grant.Proposal.Action.ActionId, mutation.MutationId, StringComparison.Ordinal)
            && string.Equals(grant.Proposal.Action.ActionFingerprint, mutation.Fingerprint, StringComparison.Ordinal);

    private void RecordOutcome(
        PreparedAccountDescriptionMutation mutation,
        SalesforceGatewayOutcome outcome,
        Synapse synapse)
    {
        var state = State;
        if (!state.InvocationRequested
            || state.Outcome is not null
            || state.Mutation is not { } prepared
            || !Equals(Origin.Source, new NeuronId(SalesforceEffectNeuron.Kind, mutation.MutationId))
            || !MatchesId(mutation)
            || !string.Equals(prepared.Fingerprint, mutation.Fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        state.Outcome = outcome;
        var approvedProposalId = state.ApprovedProposalId;
        var approvedProposalFingerprint = state.ApprovedProposalFingerprint;
        state.ApprovedProposalId = null;
        state.ApprovedProposalFingerprint = null;
        State = state;
        Emit(synapse);
        if (outcome == SalesforceGatewayOutcome.OutcomeUncertain
            && approvedProposalId is not null
            && approvedProposalFingerprint is not null)
        {
            Emit(
                new ApprovalMutationOutcomeUncertain(
                    approvedProposalId,
                    approvedProposalFingerprint),
                Dispatch.Direct(new NeuronId(ApprovalNeuron.Kind, approvedProposalId)));
        }
    }
}
