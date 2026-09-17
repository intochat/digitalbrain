namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("db.ai.agent-model-selection")]
public sealed record AgentModelSelection(
    [property: Id(0)] string? Profile = null,
    [property: Id(1)] string? Provider = null,
    [property: Id(2)] string? Model = null,
    [property: Id(3)] string? Reasoning = null,
    [property: Id(4)] int? MaxOutputTokens = null,
    [property: Id(5)] LlmCapabilities? Capabilities = null);

// Only non-secret execution settings are persisted. Provider credentials are resolved at use.
[GenerateSerializer]
[Alias("db.ai.resolved-agent-model")]
public sealed record ResolvedAgentModel(
    [property: Id(0)] string Provider,
    [property: Id(1)] string Model,
    [property: Id(2)] string? Endpoint,
    [property: Id(3)] string? Profile,
    [property: Id(4)] string Revision,
    [property: Id(5)] LlmCapabilities Capabilities,
    [property: Id(6)] string? Reasoning,
    [property: Id(7)] int? MaxOutputTokens);
