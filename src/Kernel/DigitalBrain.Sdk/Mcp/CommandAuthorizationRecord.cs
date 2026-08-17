using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.command-authorization-record")]
internal sealed record CommandAuthorizationRecord(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string ServerDisplayName,
    [property: Id(3)] Uri SignInUrl,
    [property: Id(4)] string State,
    [property: Id(5)] PendingAuthorizationOutcome Outcome,
    [property: Id(6)] NeuronId? CompletionTarget = null,
    [property: Id(7)] bool CompletionNotified = false,
    [property: Id(8)] NeuronId? RequestingNeuron = null,
    [property: Id(9)] ActorContext? Actor = null,
    [property: Id(10)] DateTimeOffset ExpiresAt = default,
    [property: Id(11)] bool Consumed = false);
