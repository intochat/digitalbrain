using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Testing;

/// <summary>
/// Test-only ingress that simulates an arbitrary module emitting a shape-valid
/// approval grant. The downstream mutation must reject it because it was not
/// produced by the approval behavior for the proposal.
/// </summary>
public sealed record ForgeApprovalGranted : Synapse
{
    public ForgeApprovalGranted(ApprovalGranted grant)
    {
        Grant = grant ?? throw new ArgumentNullException(nameof(grant));
    }

    public ApprovalGranted Grant { get; }
}

public sealed class ForgedApprovalGrantEmitter : Neuron, INeuron<ForgeApprovalGranted>
{
    public const string Kind = "forged-approval-grant";

    public Task HandleAsync(ForgeApprovalGranted synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        Emit(
            synapse.Grant,
            Dispatch.Direct(synapse.Grant.Proposal.Action.ExecutionTarget));
        return Task.CompletedTask;
    }
}
