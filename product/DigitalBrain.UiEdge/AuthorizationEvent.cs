namespace DigitalBrain.UiEdge;

internal sealed record AuthorizationEvent(
    long Sequence,
    string Kind,
    string CommandId,
    string ServerKey,
    string? ServerDisplayName,
    string? SignInUrl,
    string State,
    DateTimeOffset Timestamp);
