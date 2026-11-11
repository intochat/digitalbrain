namespace DigitalBrain.Operations;

/// <summary>Reconciles operations whose driver may have stopped.</summary>
public interface IOperationReconciliation
{
    /// <summary>Reconciles the operation driver.</summary>
    Task Reconcile(OperationId id, CancellationToken ct = default);
}

/// <summary>Recovery for host adapters projecting durable operations into a UI.</summary>
public static class OperationRecovery
{
    /// <summary>Reconciles the recorded driver's lifetime before reading operation history. Other hosts are unchanged.</summary>
    public static Task Reconcile(this IOperations operations, OperationId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ct.ThrowIfCancellationRequested();
        return operations is IOperationReconciliation reconciler ? reconciler.Reconcile(id, ct) : Task.CompletedTask;
    }
}
