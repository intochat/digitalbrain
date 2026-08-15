using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.bind-authorization-completion-target")]
public sealed record BindMcpAuthorizationCompletionTarget(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId CompletionTarget);

