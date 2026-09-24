using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core;
using DigitalBrain.MyData;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Connections;

// The owner's connection registry. A credential is written to the owner's My Data vault and the
// record holds only its SecretRef; the value is released only inside the read-only probe.
[GrainType(ConnectionsNames.NeuronType)]
internal sealed class ConnectionsNeuron : Neuron<ConnectionsState>, IConnections
{
    private readonly IGrainFactory _grains;
    private readonly ISecretResolver _secrets;
    private readonly IConnectionProbe _probe;
    private readonly TimeProvider _time;

    public ConnectionsNeuron(
        [PersistentState("connections", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ConnectionsState> store,
        IGrainFactory grains,
        ISecretResolver secrets,
        IConnectionProbe probe,
        TimeProvider time) : base(store)
    {
        _grains = grains;
        _secrets = secrets;
        _probe = probe;
        _time = time;
    }

    public Task<ConnectionRecord[]> List(CancellationToken cancellationToken = default)
    {
        var records = Snapshot.Connections.Values
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(records);
    }

    public async Task<ConnectionRecord> Connect(ConnectConnection request, CallerContext caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = ConnectionSources.Require(request.Source);
        if (string.IsNullOrWhiteSpace(request.ConnectionId))
        {
            throw new ArgumentException("A connection id is required.", nameof(request));
        }

        var credential = await StoreOrReferenceAsync(source, request, caller, cancellationToken);
        var result = await RunProbeAsync(source, credential, caller, cancellationToken);
        var record = new ConnectionRecord
        {
            Id = request.ConnectionId,
            Source = source,
            WorkspaceId = caller.WorkspaceId,
            Credential = credential,
            Status = ToStatus(result.Outcome),
            LastProbedAt = _time.GetUtcNow(),
        };
        Snapshot.Connections[record.Id] = record;
        await Save(Snapshot, new ConnectionStatusChanged(record.Id, record.Source, record.Status));
        return record;
    }

    public async Task<ConnectionRecord> Probe(string connectionId, CallerContext caller, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (!Snapshot.Connections.TryGetValue(connectionId, out var current))
        {
            throw new ConnectionNotConfiguredException($"Connection '{connectionId}' is not configured.");
        }

        var result = await RunProbeAsync(current.Source, current.Credential, caller, cancellationToken);
        var record = current with { Status = ToStatus(result.Outcome), LastProbedAt = _time.GetUtcNow() };
        Snapshot.Connections[connectionId] = record;
        await Save(Snapshot, new ConnectionStatusChanged(record.Id, record.Source, record.Status));
        return record;
    }

    public async Task Disconnect(string connectionId, CallerContext caller, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (!Snapshot.Connections.Remove(connectionId))
        {
            return;
        }

        await Save(Snapshot, new ConnectionDisconnected(connectionId));
    }

    private async Task<SecretRef> StoreOrReferenceAsync(string source, ConnectConnection request, CallerContext caller, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SecretReference))
        {
            if (!SecretRef.IsReference(request.SecretReference))
            {
                throw new ArgumentException("SecretReference must be an existing vault reference (secret://…).", nameof(request));
            }

            return new SecretRef { Reference = request.SecretReference, Label = request.Label ?? source, Status = SecretStatus.Set };
        }

        if (string.IsNullOrWhiteSpace(request.Value))
        {
            throw new ArgumentException("Provide a connection value or an existing vault secret reference.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(caller.PrincipalId))
        {
            throw new ArgumentException("A principal is required to store a connection credential.", nameof(caller));
        }

        var vault = _grains.GetGrain<IVault>(caller.PrincipalId);
        var fieldPath = FieldPath(source, request.ConnectionId);
        return await vault.SetSecret(caller, fieldPath, request.Label ?? source, request.Value, cancellationToken);
    }

    private async Task<ConnectionProbeResult> RunProbeAsync(string source, SecretRef credential, CallerContext caller, CancellationToken cancellationToken)
    {
        string value;
        try
        {
            value = await _secrets.ResolveAsync(credential, Platform(caller), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ConnectionProbeResult.Failing("The stored credential could not be resolved.");
        }

        try
        {
            return await _probe.ProbeAsync(source, value, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ConnectionProbeResult.Failing("The read-only probe failed.");
        }
    }

    // The probe is the outbound call, so it runs as trusted platform code, never as the user turn.
    private static CallerContext Platform(CallerContext caller) => caller with
    {
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = string.IsNullOrEmpty(caller.AppId) ? ConnectionsNames.NeuronType : caller.AppId,
    };

    private static ConnectionStatus ToStatus(ConnectionProbeOutcome outcome) => outcome switch
    {
        ConnectionProbeOutcome.Connected => ConnectionStatus.Connected,
        ConnectionProbeOutcome.Expired => ConnectionStatus.Expired,
        _ => ConnectionStatus.Failing,
    };

    private static string FieldPath(string source, string connectionId) => $"connections.{source}.{connectionId}";
}