using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Platform.Contracts.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Platform.Identity.Grants;

[GenerateSerializer, Alias("identity.grant-store-state")]
internal sealed record GrantStoreState
{
    [Id(0)] public List<Grant> Grants { get; init; } = [];
    [Id(1)] public bool Sealed { get; init; }
}

[GrainType("identity-grants")]
internal sealed class GrantStoreNeuron : Neuron<GrantStoreState>, IGrantStore
{
    private readonly IPersistentState<GrantStoreState> _store;

    public GrantStoreNeuron(
        [PersistentState("grants", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GrantStoreState> store)
        : base(store)
        => _store = store;

    private string WorkspaceId => IdentityGrains.WorkspaceOfGrantStore(this.GetPrimaryKeyString());

    public async Task<Grant> GrantAsync(Grant grant, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Snapshot.Sealed) { throw new InvalidOperationException("Legacy grants have migrated; use the scoped authority."); }
        ArgumentNullException.ThrowIfNull(grant);
        if (string.IsNullOrWhiteSpace(grant.AppId) || string.IsNullOrWhiteSpace(grant.SemanticTypeId))
        {
            throw new ArgumentException("A grant needs an app and a semantic type.");
        }

        var next = Snapshot;
        var stored = grant with { WorkspaceId = WorkspaceId, GrantedAt = DateTimeOffset.UtcNow };
        next.Grants.RemoveAll(existing =>
            string.Equals(existing.AppId, stored.AppId, StringComparison.Ordinal)
            && string.Equals(existing.SemanticTypeId, stored.SemanticTypeId, StringComparison.Ordinal)
            && existing.Mode == stored.Mode);
        next.Grants.Add(stored);
        await _store.WriteStateAsync();
        return stored;
    }

    public async Task RevokeAsync(string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Snapshot.Sealed) { throw new InvalidOperationException("Legacy grants have migrated; use the scoped authority."); }
        var next = Snapshot;
        var removed = next.Grants.RemoveAll(existing =>
            string.Equals(existing.AppId, appId, StringComparison.Ordinal)
            && string.Equals(existing.SemanticTypeId, semanticTypeId, StringComparison.Ordinal)
            && existing.Mode == mode);
        if (removed > 0)
        {
            await _store.WriteStateAsync();
        }
    }

    public async Task SealAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Snapshot.Sealed) { return; }
        var previous = _store.State;
        _store.State = previous with { Sealed = true };
        try { await _store.WriteStateAsync(); }
        catch { _store.State = previous; throw; }
    }

    public Task<IReadOnlyList<Grant>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Grant>>([.. Snapshot.Grants.Where(grant => !grant.Revoked)]);
    }
}
