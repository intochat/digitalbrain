using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Sdk.Secrets;

[GrainType("vault")]
internal sealed class SecretsNeuron : Neuron<SecretsState>, ISecrets, ISecretKeyMigration
{
    private readonly IPersistentState<SecretsState> _state;
    private readonly SecretsStore _secrets;
    private readonly IKeyWrapper _keys;

    public SecretsNeuron(
        [PersistentState("vault", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SecretsState> state,
        IKeyWrapper keys) : base(state)
    {
        _state = state;
        _keys = keys;
        _secrets = new SecretsStore(keys);
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (_keys is DataProtectionKeyWrapper && !string.IsNullOrEmpty(Snapshot.WrappedOwnerKey)
            && DataProtectionKeyWrapper.NeedsMigration(Snapshot.WrappedOwnerKey))
        {
            var previous = Snapshot.WrappedOwnerKey;
            var key = _keys.Unwrap(previous);
            try
            {
                Snapshot.WrappedOwnerKey = _keys.Wrap(key);
                await _state.WriteStateAsync();
            }
            catch { Snapshot.WrappedOwnerKey = previous; throw; }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
        }
    }

    public Task<bool> EnsurePortable() => Task.FromResult(string.IsNullOrEmpty(Snapshot.WrappedOwnerKey)
        || !DataProtectionKeyWrapper.NeedsMigration(Snapshot.WrappedOwnerKey));

    public async Task<SecretRef> Set(CallerContext caller, string name, string label, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.Kind == CallerKind.User && caller.PrincipalId != this.GetPrimaryKeyString())
        {
            throw new InvalidOperationException("Only the owner can store a secret.");
        }

        Snapshot.Owner = this.GetPrimaryKeyString();
        var reference = _secrets.Set(Snapshot, name, label, value);
        await _state.WriteStateAsync();
        return reference;
    }

    public Task<string> Resolve(CallerContext caller, SecretRef secret, CancellationToken cancellationToken = default)
    {
        if (caller is null || caller.Kind == CallerKind.User
            || caller.StampedBy is not (TrustedEdge.AppProxy or TrustedEdge.Platform)
            || string.IsNullOrEmpty(caller.AppId))
        {
            throw new InvalidOperationException("Only a trusted outbound call can resolve a secret.");
        }

        Snapshot.Owner = this.GetPrimaryKeyString();
        return Task.FromResult(_secrets.Resolve(Snapshot, secret));
    }
}
