namespace DigitalBrain.Product.Approvals;

public sealed class ApprovalDecisionIngress : Neuron, INeuron<ApprovalDecisionSubmitted>
{
    public const string Kind = "approval-decision-ingress";

    public Task HandleAsync(ApprovalDecisionSubmitted synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Origin.IsExternalIngress
            || !string.Equals(Id.Name, Origin.Source.Name, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(
            new ApprovalDecisionRequested(
                synapse.ProposalId,
                synapse.ExpectedProposalFingerprint,
                synapse.DecisionId,
                synapse.Decision,
                Origin.Source.Name,
                Origin.OccurredAt),
            Dispatch.Direct(new NeuronId(ApprovalNeuron.Kind, synapse.ProposalId)));
        return Task.CompletedTask;
    }
}
