namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.node-runtime")]
public sealed record BehaviorNodeRuntime(
    [property: Id(0)] string BehaviorId,
    [property: Id(1)] long Version,
    [property: Id(2)] BehaviorNode Node,
    [property: Id(3)] string Trigger,
    [property: Id(4)] string? RunId,
    [property: Id(5)] string Status,
    [property: Id(6)] string OutputJson,
    [property: Id(7)] string? Error,
    [property: Id(8)] string InputJson = "null",
    [property: Id(9)] IReadOnlyList<string>? Predecessors = null,
    [property: Id(10)] string IncomingJson = "{}");
