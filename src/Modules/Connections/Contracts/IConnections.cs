using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Connections;

public static class ConnectionsNames
{
    public const string NeuronType = "connections";
    public const string DefaultNeuron = "connections";
}

// The owner's registry of external sources. A credential is written to the My Data vault as a
// SecretRef and is resolved only inside the probe that makes the outbound call.
[Alias("connections")]
[Orleans.Metadata.DefaultGrainType(ConnectionsNames.NeuronType)]
public interface IConnections : INeuron
{
    Task<ConnectionRecord[]> List(CancellationToken cancellationToken = default);

    Task<ConnectionRecord> Connect(ConnectConnection request, CallerContext caller, CancellationToken cancellationToken = default);

    Task<ConnectionRecord> Probe(string connectionId, CallerContext caller, CancellationToken cancellationToken = default);

    Task Disconnect(string connectionId, CallerContext caller, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("connections.connect")]
public sealed record ConnectConnection
{
    [Id(0)] public required string Source { get; init; }
    [Id(1)] public required string ConnectionId { get; init; }
    [Id(2)] public string? Label { get; init; }

    // Either a pasted connection string / OAuth token (written to the vault here) ...
    [Id(3)] public string? Value { get; init; }

    // ... or an existing vault reference the owner already created.
    [Id(4)] public string? SecretReference { get; init; }
}

[GenerateSerializer, Alias("connections.status-changed")]
public sealed record ConnectionStatusChanged(string ConnectionId, string Source, ConnectionStatus Status) : Signal;

[GenerateSerializer, Alias("connections.disconnected")]
public sealed record ConnectionDisconnected(string ConnectionId) : Signal;