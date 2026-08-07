using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Time;

public sealed class ProposalDeadlineNeuron : Neuron, INeuron<ApprovalPending>
{
    public const string Kind = "proposal-deadline";

    private readonly IProposalDeadlineScheduler scheduler;

    public ProposalDeadlineNeuron(IProposalDeadlineScheduler scheduler)
    {
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public async Task HandleAsync(ApprovalPending synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesApprovalOrigin(synapse.ProposalId))
        {
            return;
        }

        var deadline = new ProposalDeadline(
            synapse.ProposalId,
            synapse.ProposalFingerprint,
            synapse.ExpiresAt);
        await scheduler.ScheduleAsync(deadline, cancellationToken);
        Emit(new ProposalDeadlineArmed(deadline));
    }

    private bool MatchesApprovalOrigin(string proposalId)
        => string.Equals(Id.Name, proposalId, StringComparison.Ordinal)
            && Origin.Source == new NeuronId(ApprovalNeuron.Kind, proposalId);
}
