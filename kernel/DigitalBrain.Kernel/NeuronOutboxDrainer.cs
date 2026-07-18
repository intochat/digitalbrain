using DigitalBrain;

namespace DigitalBrain.Kernel;

public static class NeuronOutboxDrainer
{
    public static async Task DrainAsync(
        Neuron neuron,
        NeuronDurableState state,
        Func<CancellationToken, Task> commitAsync,
        Func<NeuronNotification, Task> publishAsync,
        bool throwOnPublishFailure,
        CancellationToken cancellationToken)
    {
        var pendingIds = state.Outbox
            .Where(entry => entry.Value.DeliveryStatus == NotificationDeliveryStatus.Pending)
            .Select(entry => entry.Key)
            .ToArray();

        if (pendingIds.Length == 0)
        {
            await UnregisterRecoveryAsync(neuron, throwOnPublishFailure);
            return;
        }

        foreach (var notificationId in pendingIds)
        {
            if (!state.Outbox.TryGetValue(notificationId, out var notification))
                continue;

            if (notification.DeliveryStatus != NotificationDeliveryStatus.Pending)
                continue;

            var prior = notification;
            var attempted = notification with { AttemptCount = notification.AttemptCount + 1 };
            state.Outbox[notificationId] = attempted;

            try
            {
                await commitAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                state.Outbox[notificationId] = prior;
                await RegisterRecoveryAsync(neuron, throwOnPublishFailure);
                if (throwOnPublishFailure)
                    throw MapStorageFailure(exception);
                return;
            }

            try
            {
                await publishAsync(attempted);
            }
            catch (Exception exception)
            {
                await RegisterRecoveryAsync(neuron, throwOnPublishFailure);
                if (throwOnPublishFailure)
                    throw MapPublishFailure(exception);
                return;
            }

            var completed = attempted with { DeliveryStatus = NotificationDeliveryStatus.Completed };
            state.Outbox[notificationId] = completed;

            try
            {
                await commitAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                state.Outbox[notificationId] = attempted;
                await RegisterRecoveryAsync(neuron, throwOnPublishFailure);
                if (throwOnPublishFailure)
                    throw MapStorageFailure(exception);
                return;
            }
        }

        await UnregisterRecoveryAsync(neuron, throwOnPublishFailure);
    }

    private static async Task RegisterRecoveryAsync(
        Neuron neuron,
        bool throwOnFailure)
    {
        try
        {
            await NeuronReminder.RegisterOutboxRecoveryAsync(neuron);
        }
        catch (Exception exception)
        {
            if (throwOnFailure)
                throw MapStorageFailure(exception);
        }
    }

    private static async Task UnregisterRecoveryAsync(
        Neuron neuron,
        bool throwOnFailure)
    {
        try
        {
            await NeuronReminder.UnregisterOutboxRecoveryAsync(neuron);
        }
        catch (Exception exception)
        {
            if (throwOnFailure)
                throw MapStorageFailure(exception);
        }
    }

    private static Exception MapPublishFailure(Exception exception) =>
        new BrainException(
            exception is BrainException brainException
                ? brainException.FailureKind
                : NeuronFailureKind.ProviderUnavailable,
            "The notification could not be delivered.");

    private static Exception MapStorageFailure(Exception exception) =>
        new BrainException(
            exception is BrainException brainException
                ? brainException.FailureKind
                : NeuronFailureKind.StorageUnavailable,
            "Durable notification state could not be committed.");
}
