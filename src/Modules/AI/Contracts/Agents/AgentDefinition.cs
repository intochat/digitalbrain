namespace DigitalBrain.AI.Agents;

[GenerateSerializer, Alias("ai.agent-definition")]
public sealed record AgentDefinition
{
    [Id(0)] public string DisplayName { get; init; } = "Assistant";
    [Id(1)] public string Description { get; init; } = "";
    [Id(2)] public string Instructions { get; init; } = "";
    [Id(3)] public AgentModelSelection? Model { get; init; }
    [Id(4)] public IReadOnlyList<string> Tools { get; init; } = [];
    [Id(5)] public IReadOnlyList<string> Capabilities { get; init; } = [];
    [Id(6)] public IReadOnlyList<string> RoutingExamples { get; init; } = [];
    [Id(7)] public int MaxModelCalls { get; init; } = 16;
    [Id(8)] public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
    [Id(9)] public int MaxHistoryMessages { get; init; } = 256;
    [Id(10)] public InferenceOptions? Options { get; init; }
}

[GenerateSerializer, Alias("ai.agent-metadata")]
public sealed record AgentMetadata(
    [property: Id(0)] string Id,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] IReadOnlyList<string> Capabilities,
    [property: Id(4)] IReadOnlyList<string> RoutingExamples);