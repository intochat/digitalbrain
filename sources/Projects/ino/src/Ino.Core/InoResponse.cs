namespace Ino.Core;

[GenerateSerializer]
public sealed record InoResponse(
    [property: Id(0)] string Text,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] RfwPayload? Rfw,
    [property: Id(3)] bool Success,
    [property: Id(4)] string? Source);
