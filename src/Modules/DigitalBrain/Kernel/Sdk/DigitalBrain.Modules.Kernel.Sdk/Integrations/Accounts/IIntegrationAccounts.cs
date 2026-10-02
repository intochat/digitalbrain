using DigitalBrain.Contracts;

namespace DigitalBrain.Sdk.Integrations.Accounts;

public static class AccountNames
{
    // alias predates the integrations rename; persisted, do not touch
    public const string NeuronType = "connections";
}

// The owner's accounts on external integrations. Credentials are stored in SDK Secrets and records
// contain only references. Platform-only: the caller is read from the ambient stamp, and the
// HTTP endpoints are the user's way in.
[PlatformOnly]
[Alias("connections")] // alias predates the integrations rename; persisted, do not touch
[Orleans.Metadata.DefaultGrainType(AccountNames.NeuronType)]
public interface IIntegrationAccounts : INeuron
{
    Task<IntegrationAccount[]> List(CancellationToken cancellationToken = default);

    Task<IntegrationAccount> Connect(ConnectAccount request, CancellationToken cancellationToken = default);

    Task<IntegrationAccount> Probe(string connectionId, CancellationToken cancellationToken = default);

    Task Disconnect(string connectionId, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("connections.connect")] // alias predates the integrations rename; persisted, do not touch
public sealed record ConnectAccount
{
    [Id(0)] public required string IntegrationId { get; init; }
    [Id(1)] public required string ConnectionId { get; init; }
    [Id(2)] public string? Label { get; init; }

    // Either a pasted connection string / OAuth token (written to the vault here) ...
    [Id(3)] public string? Value { get; init; }

    // ... or a reference into the caller's own vault.
    [Id(4)] public string? SecretReference { get; init; }
}

// Signal aliases kept: see task-6 report; a durable subscription buffer may hold serialized signals.
[GenerateSerializer, Alias("connections.status-changed")] // alias predates the integrations rename; persisted, do not touch
public sealed record AccountStatusChanged(string ConnectionId, string IntegrationId, AccountStatus Status) : Signal;

[GenerateSerializer, Alias("connections.disconnected")] // alias predates the integrations rename; persisted, do not touch
public sealed record AccountDisconnected(string ConnectionId) : Signal;
