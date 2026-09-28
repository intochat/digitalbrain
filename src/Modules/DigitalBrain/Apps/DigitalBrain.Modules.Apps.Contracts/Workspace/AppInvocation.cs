namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.app-invocation")]
public sealed record AppInvocation(
    [property: Id(0)] Guid Id,
    [property: Id(1)] string Operation,
    [property: Id(2)] string Input,
    [property: Id(3)] InvocationStatus Status,
    [property: Id(4)] string? Output,
    [property: Id(5)] string? Error,
    [property: Id(6)] DateTimeOffset RequestedAt,
    [property: Id(7)] DateTimeOffset? CompletedAt);
