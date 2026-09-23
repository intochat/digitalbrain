namespace DigitalBrain.Identity;

public enum MemberRole
{
    Owner = 0,
    Member = 1,
}

public enum GrantMode
{
    Once = 0,
    ThisChat = 1,
    Always = 2,
}

// Semantic type ids double as permission scopes, e.g. "person.birthDate".
[GenerateSerializer, Alias("identity.grant")]
public sealed record Grant
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string SemanticTypeId { get; init; }
    [Id(2)] public required GrantMode Mode { get; init; }
    [Id(3)] public required string WorkspaceId { get; init; }
    [Id(4)] public string? ConversationId { get; init; }
    [Id(5)] public DateTimeOffset GrantedAt { get; init; }
    [Id(6)] public bool Revoked { get; init; }
}
