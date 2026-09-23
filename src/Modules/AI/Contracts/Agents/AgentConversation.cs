namespace DigitalBrain.AI.Agents;

[GenerateSerializer, Alias("ai.agent-conversation-turn")]
public sealed record AgentConversationTurn(
    [property: Id(0)] string RunId,
    [property: Id(1)] string UserText,
    [property: Id(2)] string AssistantText,
    [property: Id(3)] IReadOnlyList<string> ResultIds);

[GenerateSerializer, Alias("ai.agent-conversation-state")]
public sealed record AgentConversationState(
    [property: Id(0)] long Revision,
    [property: Id(1)] string? ActiveRunId,
    [property: Id(2)] IReadOnlyList<AgentConversationTurn> Turns,
    [property: Id(3)] string? Summary = null);

[GenerateSerializer, Alias("ai.agent-conversation-request")]
public sealed record AgentConversationRequest(
    [property: Id(0)] string RunId,
    [property: Id(1)] string Message);