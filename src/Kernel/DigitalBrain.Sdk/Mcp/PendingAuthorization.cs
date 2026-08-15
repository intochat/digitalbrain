using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.pending-authorization")]
internal sealed record PendingAuthorization(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string ServerDisplayName,
    [property: Id(3)] Uri SignInUrl,
    [property: Id(4)] string State,
    [property: Id(5)] PendingAuthorizationOutcome Outcome,
    [property: Id(6)] string? Code,
    [property: Id(7)] string? Iss,
    [property: Id(8)] NeuronId? CompletionTarget,
    [property: Id(9)] bool CompletionNotified,
    [property: Id(10)] NeuronId? RequestingNeuron,
    [property: Id(11)] ActorContext Actor,
    [property: Id(12)] string? CodeChallenge = null,
    [property: Id(13)] string? ProtectedCodeVerifier = null,
    [property: Id(14)] DateTimeOffset ExpiresAt = default,
    [property: Id(15)] bool Consumed = false);
