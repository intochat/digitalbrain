namespace DigitalBrain.AI.Agents;

public enum AgentRunStatus { Running, Completed, Failed, Cancelled, Interrupted }

[GenerateSerializer, Alias("ai.agent-usage")]
public sealed record AgentUsage(
    [property: Id(0)] long? InputTokens,
    [property: Id(1)] long? OutputTokens,
    [property: Id(2)] long? TotalTokens);

[GenerateSerializer, Alias("ai.agent-response")]
public sealed record AgentResponse(
    [property: Id(0)] string RunId,
    [property: Id(1)] string Text,
    [property: Id(2)] IReadOnlyList<AiMessage> Messages,
    [property: Id(3)] AgentUsage? Usage);

[GenerateSerializer, Alias("ai.agent-run-state")]
public sealed record AgentRunState(
    [property: Id(0)] string RunId,
    [property: Id(1)] AgentRunStatus Status,
    [property: Id(2)] long DefinitionRevision,
    [property: Id(3)] DateTimeOffset StartedAt,
    [property: Id(4)] DateTimeOffset? EndedAt = null,
    [property: Id(5)] AgentResponse? Response = null,
    [property: Id(6)] string? Error = null,
    [property: Id(7)] ResolvedAgentModel? Model = null,
    [property: Id(8)] ModelDescriptor? Capabilities = null,
    [property: Id(9)] AgentDefinition? Definition = null);

[GenerateSerializer, Alias("ai.agent-state")]
public sealed record AgentState(
    [property: Id(0)] long Revision,
    [property: Id(1)] AgentDefinition Definition,
    [property: Id(2)] AgentRunState? LastRun,
    [property: Id(3)] int HistoryMessageCount);

[GenerateSerializer, Alias("ai.agent-event")]
public sealed record AgentEvent(
    [property: Id(0)] long Sequence,
    [property: Id(1)] string RunId,
    [property: Id(2)] string Kind,
    [property: Id(3)] DateTimeOffset At,
    [property: Id(4)] string? CallId = null,
    [property: Id(5)] string? Tool = null);