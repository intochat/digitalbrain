using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Authority;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace DigitalBrain.Platform.Identity.Directory;

[GenerateSerializer, Alias("identity.directory-state")]
internal sealed record IdentityDirectoryState
{
    [Id(0)] public List<Account> Accounts { get; init; } = [];
    [Id(1)] public List<Member> Members { get; init; } = [];
    [Id(2)] public List<Invitation> Invitations { get; init; } = [];
    [Id(3)] public Dictionary<string, string> PasswordHashes { get; init; } = [];
    [Id(4)] public bool MigrationComplete { get; init; }
    [Id(5)] public string? MigrationPlanId { get; init; }
}

// Compatibility facade and one-time migration coordinator. Normal authentication and access
// use directly keyed actors; retained legacy records are never an authorization fallback.
[GrainType("identity-directory")]
internal sealed class IdentityDirectoryNeuron(
    [PersistentState("directory", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<IdentityDirectoryState> store,
    IOptions<IdentityMigrationOptions> migration, DeploymentStorageIdentity storageIdentity) : Neuron<IdentityDirectoryState>(store), IIdentityDirectory
{
    private readonly IPersistentState<IdentityDirectoryState> _store = store;

    public Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Snapshot.MigrationComplete && (HasLegacyState || Snapshot.MigrationPlanId is not null))
        { throw new InvalidOperationException("Legacy identity state requires an explicit maintenance migration before normal startup."); }
        return Task.CompletedTask;
    }

    private bool HasLegacyState => Snapshot.Accounts.Count + Snapshot.Members.Count
        + Snapshot.Invitations.Count + Snapshot.PasswordHashes.Count + migration.Value.LegacyBrainAccounts.Count > 0;

    public async Task<string> InspectMigrationAsync(CancellationToken cancellationToken = default)
        => (await Inspect(cancellationToken)).Id;

    public async Task ApplyMigrationAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (!migration.Value.Maintenance)
        { throw new InvalidOperationException("Migration requires maintenance mode with all old writers stopped."); }
        // Old completed checkpoints remain authoritative, even without a new plan ID.
        if (Snapshot.MigrationComplete)
        {
            if (Snapshot.MigrationPlanId is { } completed && completed != planId)
            { throw new InvalidOperationException("The completed migration belongs to another plan."); }
            await storageIdentity.BindAsync(cancellationToken);
            return;
        }
        var plan = await Inspect(cancellationToken);
        if (plan.Id != planId || (Snapshot.MigrationPlanId is { } started && started != planId))
        { throw new InvalidOperationException("Migration source or mapping changed; restore the snapshot or use the original plan."); }
        await storageIdentity.ValidateAsync(cancellationToken);
        // Persist the incomplete checkpoint before adopting storage, including grant-only
        // sources with an otherwise empty directory. A crash must never enable normal startup.
        await Save(Snapshot with { MigrationPlanId = plan.Id });
        await storageIdentity.BindAsync(cancellationToken);
        foreach (var account in plan.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await GrainFactory.GetGrain<IAccount>(account.AccountId).EnsureCreated(account);
        }
        foreach (var principal in plan.Principals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await GrainFactory.GetGrain<IPrincipal>(principal.Member.PrincipalId).Import(principal.Member, principal.Hash);
        }
        foreach (var scope in plan.Scopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authority = GrainFactory.GetGrain<IBrainAuthority>(BrainScope.Create(scope.AccountId, scope.BrainId).Id);
            await authority.Initialize(scope.AccountId, scope.BrainId);
            await authority.Import(scope.Members, scope.Grants, scope.Invitations);
            await GrainFactory.GetGrain<IGrantStore>(IdentityGrains.Grants(scope.BrainId)).SealAsync(cancellationToken);
        }
        await Save(Snapshot with { MigrationComplete = true });
    }

    private async Task Save(IdentityDirectoryState next)
    {
        var previous = _store.State;
        _store.State = next;
        try { await _store.WriteStateAsync(); }
        catch { _store.State = previous; throw; }
    }

    private async Task<IdentityMigrationPlan> Inspect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = IdentityMigrationPlan.Create(Snapshot, migration.Value);
        foreach (var scope in plan.Scopes)
        {
            var grants = await GrainFactory.GetGrain<IGrantStore>(IdentityGrains.Grants(scope.BrainId)).ListAsync(cancellationToken);
            scope.Grants = grants.OrderBy(g => g.AppId, StringComparer.Ordinal)
                .ThenBy(g => g.SemanticTypeId, StringComparer.Ordinal).ThenBy(g => g.Mode)
                .ThenBy(g => g.ConversationId, StringComparer.Ordinal).ToArray();
            if (scope.Grants.Any(g => g.WorkspaceId != scope.BrainId || string.IsNullOrWhiteSpace(g.AppId)
                || string.IsNullOrWhiteSpace(g.SemanticTypeId) || !Enum.IsDefined(g.Mode)))
            { throw new InvalidOperationException("Legacy grant scope conflicts with its storage key."); }
        }
        plan.ComputeId(migration.Value.SourceSnapshotId);
        return plan;
    }
    public async Task<Member> RegisterAsync(string principalId, string password, string displayName, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);
        return await GrainFactory.GetGrain<IPrincipal>(principalId).Register(password, displayName);
    }
    public async Task<Member?> AuthenticateAsync(string principalId, string password, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);
        if (string.IsNullOrEmpty(principalId)) { return null; }
        return await GrainFactory.GetGrain<IPrincipal>(principalId).Authenticate(password);
    }
    public async Task<Member?> FindMemberAsync(string principalId, string accountId, string brainId, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);
        return await GrainFactory.GetGrain<IBrainAuthority>(BrainScope.Create(accountId, brainId).Id).Membership(principalId);
    }
    public async Task<bool> CanAccessAsync(string principalId, string accountId, string brainId, CancellationToken cancellationToken = default)
        => await FindMemberAsync(principalId, accountId, brainId, cancellationToken) is not null;
    public async Task<Member> ShareBrainAsync(string accountId, string brainId, string principalId, string displayName, MemberRole role, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);
        var member = new Member { AccountId = accountId, BrainId = brainId, PrincipalId = principalId, DisplayName = displayName, Role = role, JoinedAt = DateTimeOffset.UtcNow };
        var authority = GrainFactory.GetGrain<IBrainAuthority>(BrainScope.Create(accountId, brainId).Id);
        await authority.EnsureMember(member);
        return (await authority.Membership(principalId))!;
    }
}
