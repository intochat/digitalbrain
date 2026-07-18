namespace Brain.Contracts;

[GenerateSerializer]
[Alias(nameof(NeuronNotification))]
public sealed record NeuronNotification(
    [property: Id(0)] Guid NotificationId,
    [property: Id(1)] Guid OperationId);
