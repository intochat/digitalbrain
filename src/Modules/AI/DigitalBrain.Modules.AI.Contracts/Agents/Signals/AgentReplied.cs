using DigitalBrain.Contracts;

namespace DigitalBrain.AI.Agents.Signals;

[GenerateSerializer, Alias("ai.agent-replied")]
public sealed record AgentReplied(
    [property: Id(0)] string AgentId,
    [property: Id(1)] string Text,
    [property: Id(2)] DateTimeOffset RepliedAt) : Signal;