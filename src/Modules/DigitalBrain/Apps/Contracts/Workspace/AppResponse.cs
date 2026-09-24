namespace DigitalBrain.Apps;

// Error set means the behavior could not complete the invocation.
[GenerateSerializer, Alias("apps.app-response")]
public sealed record AppResponse([property: Id(0)] Guid InvocationId, [property: Id(1)] string? Output, [property: Id(2)] string? Error);
