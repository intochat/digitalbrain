using DigitalBrain.Contracts;
using DigitalBrain.Neurons;

namespace DigitalBrain.Operations;

/// <summary>The actor contract for a durable operation neuron.</summary>
[NeuronAlias("runtime.operation")]
public interface IOperationNeuron
{
    /// <summary>Begins the operation entity with its initial state.</summary>
    Task<OperationState> Begin(OperationState initial);

    /// <summary>Informs the operation neuron of the required approval, if any, and begins driving.</summary>
    Task Prepared(OperationApproval? approval);

    /// <summary>Reports progress fraction 0..1.</summary>
    Task<bool> Progress(double fraction);

    /// <summary>Completes the operation with its terminal result.</summary>
    Task<OperationState> Complete(OperationResult result);

    /// <summary>Requests cancellation of this operation.</summary>
    Task<bool> RequestCancel();

    /// <summary>Reconciles operation driver lifetime and pending approvals across host restarts.</summary>
    Task Reconcile();

    /// <summary>Gets the current operation state, or null if uninitialized.</summary>
    [ReadOnlyCall]
    Task<OperationState?> Get();
}
