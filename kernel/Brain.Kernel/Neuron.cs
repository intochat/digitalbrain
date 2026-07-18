using Brain.Contracts;
using Orleans.Journaling;
using Orleans.Runtime;

namespace Brain.Kernel;

public abstract class Neuron([NeuronState] NeuronDurableState durableState) : DurableGrain, IRemindable
{
    protected NeuronDurableState DurableState { get; } = durableState;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await RecoverPendingOperationsAsync(cancellationToken);
        await DrainOutboxCoreAsync(throwOnPublishFailure: false, cancellationToken);
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != NeuronReminder.OutboxRecoveryName)
            return Task.CompletedTask;

        return DrainOutboxCoreAsync(throwOnPublishFailure: false);
    }

    protected async Task CommitDurableStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PersistDurableStateAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not BrainException)
        {
            throw new BrainException(NeuronFailureKind.StorageUnavailable, exception.Message);
        }
    }

    protected virtual async Task PersistDurableStateAsync(CancellationToken cancellationToken) =>
        await WriteStateAsync(cancellationToken);

    protected async Task RecoverPendingOperationsAsync(CancellationToken cancellationToken = default)
    {
        var changed = false;
        foreach (var (operationId, operation) in DurableState.Operations.ToArray())
        {
            if (operation.Status != ExternalOperationStatus.Pending)
                continue;

            DurableState.Operations[operationId] = ExternalOperationTransitions.Apply(
                operation,
                new ExternalOperationTransition.Unknown(NeuronFailureKind.OperationUnknown));
            changed = true;
        }

        if (changed)
            await CommitDurableStateAsync(cancellationToken);
    }

    protected Task DrainOutboxCoreAsync(
        bool throwOnPublishFailure,
        CancellationToken cancellationToken = default) =>
        NeuronOutboxDrainer.DrainAsync(
            this,
            DurableState,
            CommitDurableStateAsync,
            PublishNotificationAsync,
            throwOnPublishFailure,
            cancellationToken);

    protected virtual Task PublishNotificationAsync(NeuronNotification notification) =>
        NeuronNotificationPublisher.PublishAsync(this, notification);
}
