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
            return;

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
                await NeuronReminder.RegisterOutboxRecoveryAsync(neuron);
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
                await NeuronReminder.RegisterOutboxRecoveryAsync(neuron);
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
                await NeuronReminder.RegisterOutboxRecoveryAsync(neuron);
                if (throwOnPublishFailure)
                    throw MapStorageFailure(exception);
                return;
            }
        }

        await NeuronReminder.UnregisterOutboxRecoveryAsync(neuron);
    }

    private static Exception MapPublishFailure(Exception exception) =>
        exception is BrainException
            ? exception
            : new BrainException(NeuronFailureKind.ProviderUnavailable, exception.Message);

    private static Exception MapStorageFailure(Exception exception) =>
        exception is BrainException
            ? exception
            : new BrainException(NeuronFailureKind.StorageUnavailable, exception.Message);
}
