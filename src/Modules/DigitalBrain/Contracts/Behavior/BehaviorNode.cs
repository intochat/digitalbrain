using System.Text.Json;


namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.node")]
public sealed record BehaviorNode(
    [property: Id(0)] string Id,
    [property: Id(1)] string Kind,
    [property: Id(2)] JsonElement Config,
    [property: Id(3)] string InputType = "Json",
    [property: Id(4)] string OutputType = "Json");
