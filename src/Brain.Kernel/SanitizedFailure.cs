namespace Brain.Kernel;

[GenerateSerializer, Alias("brain.sanitized-failure.v1")]
public sealed record SanitizedFailure(
    [property: Id(0)] Guid FailureId,
    [property: Id(1)] string Code,
    [property: Id(2)] string Message,
    [property: Id(3)] DateTimeOffset OccurredAt,
    [property: Id(4)] Guid? CommandId,
    [property: Id(5)] Guid? EventId);
