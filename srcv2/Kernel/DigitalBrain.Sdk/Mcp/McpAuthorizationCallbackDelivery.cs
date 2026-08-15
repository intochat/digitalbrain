using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.authorization-callback-delivery")]
public sealed record McpAuthorizationCallbackDelivery(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] bool Completed,
    [property: Id(2)] bool Denied);

