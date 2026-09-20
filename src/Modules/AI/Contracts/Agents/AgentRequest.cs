namespace DigitalBrain.AI.Agents;

[GenerateSerializer, Alias("db.ai.agent-request")]
public sealed record AgentRequest(
    [property: Id(0)] string Message,
    [property: Id(1)] AgentModelSelection? Model = null,
    [property: Id(2)] string? System = null);