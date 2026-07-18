namespace Brain.Contracts;

public enum NotificationDeliveryStatus
{
    Pending,
    Completed
}

[GenerateSerializer]
[Alias(nameof(NeuronNotification))]
public sealed record NeuronNotification(
    [property: Id(0)] Guid NotificationId,
    [property: Id(1)] Guid OperationId,
    [property: Id(2)] NotificationDeliveryStatus DeliveryStatus,
    [property: Id(3)] int AttemptCount);
