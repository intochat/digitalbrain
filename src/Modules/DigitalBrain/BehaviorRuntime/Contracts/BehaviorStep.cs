using System.Text.Json;


namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.step")]
public sealed record BehaviorStep(
    [property: Id(0)] string NodeId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Status,
    [property: Id(3)] JsonElement Input,
    [property: Id(4)] JsonElement Output,
    [property: Id(5)] string? Error,
    [property: Id(6)] DateTimeOffset StartedAt,
    [property: Id(7)] DateTimeOffset CompletedAt);
