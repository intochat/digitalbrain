using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.MyData;

[GrainType("vault")]
internal sealed class VaultNeuron : Neuron<VaultState>, IVault
{
    private const int MaxAuditEntries = 512;

    private readonly IPersistentState<VaultState> _store;
    private readonly VaultStore _vault;
    private readonly TimeProvider _time;

    public VaultNeuron(
        [PersistentState("vault", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<VaultState> store,
        IKeyWrapper keyWrapper,
        TimeProvider time) : base(store)
    {
        _store = store;
        _vault = new VaultStore(keyWrapper);
        _time = time;
    }

    private string Owner => this.GetPrimaryKeyString();

    private VaultState State
    {
        get
        {
            Snapshot.Owner = Owner;
            return Snapshot;
        }
    }

    public async Task<VaultView> Read(CallerContext caller, CancellationToken cancellationToken = default)
    {
        Record(caller, "read", "");
        var view = _vault.Read(State);
        await _store.WriteStateAsync();
        return view;
    }

    public async Task<VaultFieldView> SetField(CallerContext caller, string fieldPath, FieldKind kind, string value, CancellationToken cancellationToken = default)
    {
        Record(caller, "field.set", fieldPath);
        var record = await _vault.SetFieldAsync(State, fieldPath, kind, value);
        await Save(State, new VaultFieldChanged { Owner = Owner, FieldPath = record.FieldPath, Kind = record.Kind, IsSecret = false });
        return new VaultFieldView
        {
            FieldPath = record.FieldPath,
            Kind = record.Kind,
            Sensitivity = TypeCatalog.Get(record.Kind).Sensitivity.ToString(),
            IsSet = record.IsSet,
            Display = "••••",
            IsSecret = false,
        };
    }

    public async Task<SecretRef> SetSecret(CallerContext caller, string fieldPath, string label, string value, CancellationToken cancellationToken = default)
    {
        Record(caller, "secret.set", fieldPath);
        var secret = await _vault.SetSecretAsync(State, fieldPath, label, value);
        await Save(State, new VaultFieldChanged { Owner = Owner, FieldPath = fieldPath, Kind = FieldKind.Secret, IsSecret = true });
        return secret;
    }

    public async Task<VaultExport> Export(CallerContext caller, CancellationToken cancellationToken = default)
    {
        Record(caller, "export", "");
        var export = _vault.Export(State);
        await _store.WriteStateAsync();
        return export;
    }

    public async Task Erase(CallerContext caller, CancellationToken cancellationToken = default)
    {
        Record(caller, "erase", "");
        _vault.Erase(State);
        await Save(State, new VaultErased { Owner = Owner });
    }

    public Task<VaultAuditEntry[]> Audit(CallerContext caller, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        var entries = State.Audit
            .TakeLast(limit <= 0 ? 0 : limit)
            .Reverse()
            .Select(entry => new VaultAuditEntry
            {
                Action = entry.Action,
                FieldPath = entry.FieldPath,
                PrincipalId = entry.PrincipalId,
                AppId = entry.AppId,
                At = entry.At,
            })
            .ToArray();
        return Task.FromResult(entries);
    }

    public async Task<string> ResolveSecret(CallerContext caller, SecretRef secret, CancellationToken cancellationToken = default)
    {
        SecretResolutionPolicy.EnsureAllowed(caller);
        Record(caller, "secret.resolve", VaultStore.FieldPathOf(secret));
        var value = await _vault.ResolveSecretAsync(State, secret);
        await _store.WriteStateAsync();
        await PublishAsync(new VaultSecretResolved { Owner = Owner, FieldPath = VaultStore.FieldPathOf(secret), AppId = caller.AppId });
        return value;
    }

    private void Record(CallerContext caller, string action, string fieldPath)
    {
        var audit = State.Audit;
        audit.Add(new VaultAuditRecord
        {
            Action = action,
            FieldPath = fieldPath,
            PrincipalId = caller.PrincipalId,
            AppId = caller.AppId,
            At = _time.GetUtcNow(),
        });
        if (audit.Count > MaxAuditEntries)
        {
            audit.RemoveRange(0, audit.Count - MaxAuditEntries);
        }
    }
}
