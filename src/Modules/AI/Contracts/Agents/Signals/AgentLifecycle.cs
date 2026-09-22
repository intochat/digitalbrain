using DigitalBrain.Contracts;

namespace DigitalBrain.AI.Agents.Signals;

[GenerateSerializer, Alias("ai.agent-configured")]
public sealed record AgentConfigured([property: Id(0)] string AgentId, [property: Id(1)] long Revision) : Signal;

[GenerateSerializer, Alias("ai.agent-run-changed")]
public sealed record AgentRunChanged([property: Id(0)] string AgentId, [property: Id(1)] AgentRunState Run) : Signal;

[GenerateSerializer, Alias("ai.agent-output-delta")]
public sealed record AgentOutputDelta([property: Id(0)] string AgentId, [property: Id(1)] string RunId,
    [property: Id(2)] long Sequence, [property: Id(3)] string Text) : Signal;

[GenerateSerializer, Alias("ai.agent-tool-call-changed")]
public sealed record AgentToolCallChanged([property: Id(0)] string AgentId, [property: Id(1)] AgentEvent Event) : Signal;

[GenerateSerializer, Alias("ai.agent-history-cleared")]
public sealed record AgentHistoryCleared([property: Id(0)] string AgentId, [property: Id(1)] long Revision) : Signal;