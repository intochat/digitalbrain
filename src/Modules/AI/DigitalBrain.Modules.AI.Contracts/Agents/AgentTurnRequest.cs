namespace DigitalBrain.AI.Agents;

[GenerateSerializer, Alias("ai.turn-request")]
public sealed record AgentTurnRequest([property: Id(0)] string AgentId, [property: Id(1)] string RunId, [property: Id(2)] string ScopeId,
    [property: Id(3)] IReadOnlyList<AgentConversationTurn> History, [property: Id(4)] string Message, [property: Id(5)] AgentModelSelection? Model,
    [property: Id(6)] string? Instructions = null, [property: Id(7)] IReadOnlyList<string>? ToolNames = null,
    [property: Id(8)] IReadOnlyList<AiMessage>? Messages = null, [property: Id(9)] AiMessage? Input = null,
    [property: Id(10)] bool Streaming = false, [property: Id(11)] int MaxModelCalls = 16, [property: Id(12)] TimeSpan? Timeout = null, [property: Id(13)] InferenceOptions? Options = null,
    [property: Id(14)] IReadOnlyList<string>? ContextProviders = null);
