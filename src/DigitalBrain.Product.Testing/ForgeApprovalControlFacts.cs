using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Testing;

/// <summary>
/// Test-only input used to prove that approval control facts are origin-fenced.
/// </summary>
public sealed record ForgeApprovalDecision(ApprovalDecisionRequested Decision) : Synapse;

public sealed record ForgeApprovalDeadline(ApprovalDeadlineObserved Deadline) : Synapse;

public sealed class ForgedApprovalControlEmitter : Neuron,
    INeuron<ForgeApprovalDecision>,
    INeuron<ForgeApprovalDeadline>
{
    public const string Kind = "forged-approval-control";

    public Task HandleAsync(ForgeApprovalDecision synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        Emit(
            synapse.Decision,
            Dispatch.Direct(new NeuronId(ApprovalNeuron.Kind, synapse.Decision.ProposalId)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ForgeApprovalDeadline synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        Emit(
            synapse.Deadline,
            Dispatch.Direct(new NeuronId(ApprovalNeuron.Kind, synapse.Deadline.ProposalId)));
        return Task.CompletedTask;
    }
}
