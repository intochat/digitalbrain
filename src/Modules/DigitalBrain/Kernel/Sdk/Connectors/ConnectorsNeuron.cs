using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Secrets;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Sdk.Connectors;

// The owner's connection registry. A credential is written to the shared secrets grain and the
// record holds only its SecretRef; the value is released only inside the read-only probe.
[GrainType(ConnectorNames.NeuronType)]
internal sealed class ConnectorsNeuron : Neuron<ConnectorsState>, IConnectors
{
    private readonly IGrainFactory _grains;
    private readonly IConnectorProbe _probe;
    private readonly TimeProvider _time;

    public ConnectorsNeuron(
        [PersistentState("connections", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ConnectorsState> store,
        IGrainFactory grains,
        IConnectorProbe probe,
        TimeProvider time) : base(store)
    {
        _grains = grains;
        _probe = probe;
        _time = time;
    }

    public Task<ConnectorRecord[]> List(CancellationToken cancellationToken = default)
    {
        var records = Snapshot.Connections.Values
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(records);
    }

    public async Task<ConnectorRecord> Connect(ConnectRequest request, CallerContext caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        var source = request.Source.Trim();
        if (string.IsNullOrWhiteSpace(request.ConnectionId))
        {
            throw new ArgumentException("A connection id is required.", nameof(request));
        }

        var credential = await StoreOrReferenceAsync(source, request, caller, cancellationToken);
        var result = await RunProbeAsync(source, credential, caller, cancellationToken);
        var record = new ConnectorRecord
        {
            Id = request.ConnectionId,
            Source = source,
            WorkspaceId = caller.WorkspaceId,
            Credential = credential,
            Status = ToStatus(result.Outcome),
            LastProbedAt = _time.GetUtcNow(),
        };
        Snapshot.Connections[record.Id] = record;
        await Save(Snapshot, new ConnectorStatusChanged(record.Id, record.Source, record.Status));
        return record;
    }

    public async Task<ConnectorRecord> Probe(string connectionId, CallerContext caller, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (!Snapshot.Connections.TryGetValue(connectionId, out var current))
        {
            throw new ConnectorNotConfiguredException($"Connection '{connectionId}' is not configured.");
        }

        var result = await RunProbeAsync(current.Source, current.Credential, caller, cancellationToken);
        var record = current with { Status = ToStatus(result.Outcome), LastProbedAt = _time.GetUtcNow() };
        Snapshot.Connections[connectionId] = record;
        await Save(Snapshot, new ConnectorStatusChanged(record.Id, record.Source, record.Status));
        return record;
    }

    public async Task Disconnect(string connectionId, CallerContext caller, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (!Snapshot.Connections.Remove(connectionId))
        {
            return;
        }

        await Save(Snapshot, new ConnectorDisconnected(connectionId));
    }

    private async Task<SecretRef> StoreOrReferenceAsync(string source, ConnectRequest request, CallerContext caller, CancellationToken cancellationToken)
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

        var vault = _grains.GetGrain<ISecrets>(caller.PrincipalId);
        var fieldPath = FieldPath(source, request.ConnectionId);
        return await vault.Set(caller, fieldPath, request.Label ?? source, request.Value, cancellationToken);
    }

    private async Task<ConnectorProbeResult> RunProbeAsync(string source, SecretRef credential, CallerContext caller, CancellationToken cancellationToken)
    {
        string value;
        try
        {
            value = await _grains.GetGrain<ISecrets>(credential.Owner).Resolve(Platform(caller), credential, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ConnectorProbeResult.Failing("The stored credential could not be resolved.");
        }

        try
        {
            return await _probe.ProbeAsync(source, value, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ConnectorProbeResult.Failing("The read-only probe failed.");
        }
    }

    // The probe is the outbound call, so it runs as trusted platform code, never as the user turn.
    private static CallerContext Platform(CallerContext caller) => caller with
    {
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = string.IsNullOrEmpty(caller.AppId) ? ConnectorNames.NeuronType : caller.AppId,
    };

    private static ConnectorStatus ToStatus(ConnectorProbeOutcome outcome) => outcome switch
    {
        ConnectorProbeOutcome.Connected => ConnectorStatus.Connected,
        ConnectorProbeOutcome.Expired => ConnectorStatus.Expired,
        _ => ConnectorStatus.Failing,
    };

    private static string FieldPath(string source, string connectionId) => $"connections.{source}.{connectionId}";
}

