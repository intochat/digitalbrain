using DigitalBrain;
using Orleans.Journaling;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

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
        catch (BrainException exception)
        {
            throw new BrainException(
                exception.FailureKind,
                "Durable neuron state could not be committed.");
        }
        catch (Exception)
        {
            throw new BrainException(
                NeuronFailureKind.StorageUnavailable,
                "Durable neuron state could not be committed.");
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
