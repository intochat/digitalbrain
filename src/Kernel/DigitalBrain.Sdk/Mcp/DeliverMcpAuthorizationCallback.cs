using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.deliver-authorization-callback")]
public sealed record DeliverMcpAuthorizationCallback(
    [property: Id(0)] string State,
    [property: Id(1)] string? Code,
    [property: Id(2)] string? Error,
    [property: Id(3)] string? Iss);

