namespace DigitalBrain.Contracts.Enforcement;

public enum CallerKind
{
    User = 0,
    Assistant = 1,
    App = 2,
    Scheduler = 3,
    Platform = 4,
}

public enum TrustedEdge
{
    AuthenticatedHttp = 0,
    AppProxy = 1,
    Scheduler = 2,
    Platform = 3,
}

// Stamped only at a trusted edge and re-stamped (never inherited) when a call enters or leaves an app.
[GenerateSerializer, Alias("enforcement.caller-context")]
public sealed record CallerContext
{
    [Id(0)] public required string PrincipalId { get; init; }
    [Id(1)] public required string AccountId { get; init; }
    [Id(2)] public required string WorkspaceId { get; init; }
    [Id(3)] public required CallerKind Kind { get; init; }
    [Id(4)] public required TrustedEdge StampedBy { get; init; }
    [Id(5)] public string? AppId { get; init; }
    [Id(6)] public string? IntentId { get; init; }
    [Id(7)] public string? ConversationId { get; init; }
}
