using DigitalBrain.Operations;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>Recovery for host adapters projecting durable operations into a UI.</summary>
public static class OperationRecovery
{
    /// <summary>Reconciles the recorded driver's lifetime before reading operation history. Other hosts are unchanged.</summary>
    public static Task Reconcile(this IOperations operations, OperationId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ct.ThrowIfCancellationRequested();
        return operations is RuntimeOperations runtime ? runtime.Reconcile(id, ct) : Task.CompletedTask;
    }
}
