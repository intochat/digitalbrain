using System.Text.Json;


namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.revision")]
public sealed record BehaviorRevision(
    [property: Id(0)] long Version,
    [property: Id(1)] BehaviorDefinition Definition,
    [property: Id(2)] DateTimeOffset CreatedAt);
