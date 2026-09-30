using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk.Secrets;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Sdk.Integrations.Accounts;

// The owner's account registry. A credential is written to the shared secrets grain and the
// record holds only its SecretRef; the value is released only inside the read-only probe.
[GrainType(AccountNames.NeuronType)] // alias predates the integrations rename; persisted, do not touch
internal sealed class IntegrationAccountsNeuron : Neuron<IntegrationAccountsState>, IIntegrationAccounts
{
    private readonly IGrainFactory _grains;
    private readonly IAccountProbe _probe;
    private readonly TimeProvider _time;

    public IntegrationAccountsNeuron(
        [PersistentState("connections", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<IntegrationAccountsState> store,
        IGrainFactory grains,
        IAccountProbe probe,
        TimeProvider time) : base(store)
    {
        _grains = grains;
        _probe = probe;
        _time = time;
    }

    public Task<IntegrationAccount[]> List(CancellationToken cancellationToken = default)
    {
        var records = Snapshot.Connections.Values
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(records);
    }

    public async Task<IntegrationAccount> Connect(ConnectAccount request, CancellationToken cancellationToken = default)
    {
        var caller = CallerForWrite();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IntegrationId);
        var source = request.IntegrationId.Trim();
        if (string.IsNullOrWhiteSpace(request.ConnectionId))
        {
            throw new ArgumentException("A connection id is required.", nameof(request));
        }

        var credential = await StoreOrReferenceAsync(source, request, caller, cancellationToken);
        var result = await RunProbeAsync(source, credential, caller, cancellationToken);
        var record = new IntegrationAccount
        {
            Id = request.ConnectionId,
            IntegrationId = source,
            WorkspaceId = caller.BrainId,
            Credential = credential,
            Status = ToStatus(result.Outcome),
            LastProbedAt = _time.GetUtcNow(),
        };
        Snapshot.Connections[record.Id] = record;
        await Save(Snapshot, new AccountStatusChanged(record.Id, record.IntegrationId, record.Status));
        return record;
    }

    public async Task<IntegrationAccount> Probe(string connectionId, CancellationToken cancellationToken = default)
    {
        var caller = CallerForWrite();
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (!Snapshot.Connections.TryGetValue(connectionId, out var current))
        {
            throw new AccountNotConfiguredException($"Connection '{connectionId}' is not configured.");
        }

        var result = await RunProbeAsync(current.IntegrationId, current.Credential, caller, cancellationToken);
        var record = current with { Status = ToStatus(result.Outcome), LastProbedAt = _time.GetUtcNow() };
        Snapshot.Connections[connectionId] = record;
        await Save(Snapshot, new AccountStatusChanged(record.Id, record.IntegrationId, record.Status));
        return record;
    }

    public async Task Disconnect(string connectionId, CancellationToken cancellationToken = default)
    {
        CallerForWrite();
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (!Snapshot.Connections.Remove(connectionId))
        {
            return;
        }

        await Save(Snapshot, new AccountDisconnected(connectionId));
    }

    private async Task<SecretRef> StoreOrReferenceAsync(string source, ConnectAccount request, CallerContext caller, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SecretReference))
        {
            if (!SecretRef.IsReference(request.SecretReference) || !OwnedByCaller(request.SecretReference, caller))
            {
                throw new ArgumentException("SecretReference must be an existing reference into your own vault.", nameof(request));
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
        var fieldPath = FieldPath(this.GetPrimaryKeyString(), source, request.ConnectionId);
        return await vault.Set(caller, fieldPath, request.Label ?? source, request.Value, cancellationToken);
    }

    // The vault owner named by a reference must be the caller's own principal; another owner's vault,
    // the platform vault included, is never bindable.
    private static bool OwnedByCaller(string reference, CallerContext caller)
    {
        try { return new SecretRef { Reference = reference }.Owner == caller.PrincipalId; }
        catch (InvalidOperationException) { return false; }
    }

    // The ambient stamp is the only trusted caller. Installed apps run under an App stamp and may not
    // reach the account registry whatever else they pass.
    private static CallerContext CallerForWrite()
    {
        var caller = CallerContextStamper.Require();
        if (caller.Kind == CallerKind.App || caller.StampedBy == TrustedEdge.AppProxy)
        {
            throw new InvalidOperationException("Installed apps cannot manage integration accounts.");
        }

        return caller;
    }

    private async Task<AccountProbeResult> RunProbeAsync(string source, SecretRef credential, CallerContext caller, CancellationToken cancellationToken)
    {
        string value;
        try
        {
            value = await _grains.GetGrain<ISecrets>(credential.Owner).Resolve(Platform(caller), credential, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return AccountProbeResult.Failing("The stored credential could not be resolved.");
        }

        try
        {
            return await _probe.ProbeAsync(source, value, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return AccountProbeResult.Failing("The read-only probe failed.");
        }
    }

    // The probe is the outbound call, so it runs as trusted platform code, never as the user turn.
    private static CallerContext Platform(CallerContext caller) => caller with
    {
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = string.IsNullOrEmpty(caller.AppId) ? AccountNames.NeuronType : caller.AppId,
    };

    private static AccountStatus ToStatus(AccountProbeOutcome outcome) => outcome switch
    {
        AccountProbeOutcome.Connected => AccountStatus.Connected,
        AccountProbeOutcome.Expired => AccountStatus.Expired,
        _ => AccountStatus.Failing,
    };

    private static string FieldPath(string registry, string source, string connectionId) => "connections." +
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new[] { registry, source, connectionId }))));
}

