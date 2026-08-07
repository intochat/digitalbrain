namespace DigitalBrain.Product.Approvals;

public sealed class ApprovalDeadlineIngress : Neuron, INeuron<ApprovalDeadlineElapsed>
{
    public const string Kind = "approval-deadline-ingress";

    public Task HandleAsync(ApprovalDeadlineElapsed synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Origin.IsExternalIngress
            || !string.Equals(Id.Name, Origin.Source.Name, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(
            new ApprovalDeadlineObserved(
                synapse.ProposalId,
                synapse.ExpectedProposalFingerprint,
                Origin.OccurredAt),
            Dispatch.Direct(new NeuronId(ApprovalNeuron.Kind, synapse.ProposalId)));
        return Task.CompletedTask;
    }
}
