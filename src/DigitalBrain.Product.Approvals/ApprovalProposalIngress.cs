namespace DigitalBrain.Product.Approvals;

/// <summary>
/// Converts an explicitly external proposal command into the internal approval-state fact.
/// </summary>
public sealed class ApprovalProposalIngress : Neuron, INeuron<ApprovalProposalSubmitted>
{
    public const string Kind = "approval-proposal-ingress";

    public Task HandleAsync(ApprovalProposalSubmitted synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Origin.IsExternalIngress
            || !string.Equals(Id.Name, Origin.Source.Name, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(
            new ApprovalProposed(synapse.Proposal),
            Dispatch.Direct(new NeuronId(ApprovalNeuron.Kind, synapse.Proposal.ProposalId)));
        return Task.CompletedTask;
    }
}
