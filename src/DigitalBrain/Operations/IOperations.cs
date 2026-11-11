namespace DigitalBrain.Operations;

/// <summary>Long-running operations a neuron starts from a plan: progress, completion, cancellation and dry runs.</summary>
public interface IOperations
{
    /// <summary>Starts the operation <paramref name="plan"/> describes and returns its handle.</summary>
    Task<IOperation> Start(OperationPlan plan, CancellationToken ct);

    /// <summary>Requests cancellation of a running operation; its handle's <see cref="IOperation.Cancellation"/> fires.</summary>
    Task Cancel(OperationId id, CancellationToken ct);
}

/// <summary>Read-only view of operations, for an app that shows progress (for example a Tasks list) without starting or cancelling them.</summary>
public interface IOperationsQuery
{
    /// <summary>The operation <paramref name="id"/>, running or done, or null when no operation of this brain has that id.</summary>
    Task<OperationView?> Read(OperationId id, CancellationToken ct);
}

/// <summary>The handle of a running operation.</summary>
public interface IOperation
{
    /// <summary>The operation id.</summary>
    OperationId Id { get; }

    /// <summary>Fires when the operation is cancelled.</summary>
    CancellationToken Cancellation { get; }

    /// <summary>Reports progress as a fraction between 0 and 1.</summary>
    ValueTask Report(double fraction);

    /// <summary>Completes the operation with its result.</summary>
    ValueTask Complete(OperationResult result);
}
