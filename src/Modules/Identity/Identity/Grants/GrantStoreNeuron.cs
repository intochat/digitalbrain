using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Identity.Grants;

[GenerateSerializer, Alias("identity.grant-store-state")]
internal sealed record GrantStoreState
{
    [Id(0)] public List<Grant> Grants { get; init; } = [];
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

    public Task<IReadOnlyList<Grant>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Grant>>([.. Snapshot.Grants.Where(grant => !grant.Revoked)]);
    }
}
