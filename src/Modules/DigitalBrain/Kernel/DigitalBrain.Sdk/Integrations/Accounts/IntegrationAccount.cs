using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Sdk.Integrations.Accounts;

public enum AccountStatus
{
    Connected = 0,
    Expired = 1,
    Failing = 2,
    Disconnected = 3,
}

[GenerateSerializer, Alias("connections.connection")] // alias predates the integrations rename; persisted, do not touch
public sealed record IntegrationAccount
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required string IntegrationId { get; init; }
    [Id(2)] public required string WorkspaceId { get; init; }
    [Id(3)] public required SecretRef Credential { get; init; }
    [Id(4)] public required AccountStatus Status { get; init; }
    [Id(5)] public DateTimeOffset? LastProbedAt { get; init; }
}

