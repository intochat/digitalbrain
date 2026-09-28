using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Sdk.Connectors;

public enum ConnectorStatus
{
    Connected = 0,
    Expired = 1,
    Failing = 2,
    Disconnected = 3,
}

[GenerateSerializer, Alias("connections.connection")]
public sealed record ConnectorRecord
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required string Source { get; init; }
    [Id(2)] public required string WorkspaceId { get; init; }
    [Id(3)] public required SecretRef Credential { get; init; }
    [Id(4)] public required ConnectorStatus Status { get; init; }
    [Id(5)] public DateTimeOffset? LastProbedAt { get; init; }
}

