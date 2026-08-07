using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Testing;

/// <summary>
/// Test-only ingress that simulates another product behavior attempting to
/// produce an approval-shaped pending fact. Presentation must trust only the
/// matching approval state machine as its origin.
/// </summary>
public sealed record ForgeApprovalPending(ApprovalProposal Proposal) : Synapse;

public sealed class ForgedApprovalPendingEmitter : Neuron, INeuron<ForgeApprovalPending>
{
    public const string Kind = "forged-approval-pending";

    public Task HandleAsync(ForgeApprovalPending synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        Emit(new ApprovalPending(synapse.Proposal));
        return Task.CompletedTask;
    }
}
