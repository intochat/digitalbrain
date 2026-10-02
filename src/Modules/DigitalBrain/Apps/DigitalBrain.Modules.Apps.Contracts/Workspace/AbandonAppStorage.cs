namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.abandon-storage")]
public sealed record AbandonAppStorage([property: Id(0)] Guid OperationId);

[GenerateSerializer, Alias("apps.abandoned-storage")]
public sealed record AbandonedAppStorage(
    [property: Id(0)] Guid OperationId,
    [property: Id(1)] string[] Files);
