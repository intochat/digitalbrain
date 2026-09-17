using System.Text.Json;


namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.run")]
public sealed record BehaviorRunSnapshot(
    [property: Id(0)] string RunId,
    [property: Id(1)] string BehaviorId,
    [property: Id(2)] long Version,
    [property: Id(3)] string Status,
    [property: Id(4)] JsonElement Input,
    [property: Id(5)] JsonElement Output,
    [property: Id(6)] IReadOnlyList<BehaviorStep> Steps,
    [property: Id(7)] string? Error,
    [property: Id(8)] DateTimeOffset StartedAt,
    [property: Id(9)] DateTimeOffset? CompletedAt);
