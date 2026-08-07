using DigitalBrain.Product.Approvals;

namespace DigitalBrain.Product.Testing;

public sealed class ApprovalGrantProbe : Neuron, INeuron<ApprovalGranted>
{
    public Task HandleAsync(ApprovalGranted synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
