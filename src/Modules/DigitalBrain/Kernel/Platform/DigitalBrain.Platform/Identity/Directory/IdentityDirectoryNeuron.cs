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
}

// Compatibility facade and one-time migration coordinator. Normal authentication and access
// use directly keyed actors; retained legacy records are never an authorization fallback.
[GrainType("identity-directory")]
internal sealed class IdentityDirectoryNeuron(
    [PersistentState("directory", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<IdentityDirectoryState> store,
    IOptions<IdentityMigrationOptions> migration) : Neuron<IdentityDirectoryState>(store), IIdentityDirectory
{
    private readonly IPersistentState<IdentityDirectoryState> _store = store;

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.MigrationComplete) { return; }
        var legacy = Snapshot;
        var hasLegacy = legacy.Accounts.Count + legacy.Members.Count + legacy.Invitations.Count + legacy.PasswordHashes.Count > 0;
        if ((hasLegacy || migration.Value.LegacyBrainAccounts.Count > 0) && !migration.Value.Maintenance)
        { throw new InvalidOperationException("Legacy identity state requires DigitalBrain:Identity:Migration:Maintenance=true with old writers stopped."); }
        var scopes = legacy.Members.Select(m => (m.AccountId, m.BrainId))
            .Concat(legacy.Invitations.Select(i => (i.AccountId, BrainId: i.WorkspaceId)))
            .Concat(migration.Value.LegacyBrainAccounts.Select(kv => (AccountId: kv.Value, BrainId: kv.Key))).Distinct().ToArray();
        if (scopes.GroupBy(s => s.BrainId).Any(group => group.Select(s => s.AccountId).Distinct().Count() > 1))
        { throw new InvalidOperationException("Legacy grant keys are ambiguous across accounts; resolve the mapping before migration."); }
        foreach (var account in legacy.Accounts)
        { await GrainFactory.GetGrain<IAccount>(account.AccountId).EnsureCreated(account); }
        foreach (var (principal, hash) in legacy.PasswordHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owned = legacy.Members.Where(m => m.PrincipalId == principal && legacy.Accounts.Any(a => a.AccountId == m.AccountId && a.OwnerPrincipalId == principal)).ToArray();
            var preferred = owned.Where(m => m.BrainId == "account-" + m.AccountId).ToArray();
            var member = preferred.Length == 1 ? preferred[0] : owned.Length == 1 ? owned[0]
                : throw new InvalidOperationException("A legacy principal has no unambiguous default account membership.");
            await GrainFactory.GetGrain<IPrincipal>(principal).Import(member, hash);
        }
        foreach (var scope in scopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authority = GrainFactory.GetGrain<IBrainAuthority>(BrainScope.Create(scope.AccountId, scope.BrainId).Id);
            var legacyGrants = GrainFactory.GetGrain<IGrantStore>(IdentityGrains.Grants(scope.BrainId));
            var grants = await legacyGrants.ListAsync(cancellationToken);
            await authority.Initialize(scope.AccountId, scope.BrainId);
            await authority.Import(legacy.Members.Where(m => m.AccountId == scope.AccountId && m.BrainId == scope.BrainId).ToArray(),
                grants.ToArray(), legacy.Invitations.Where(i => i.AccountId == scope.AccountId && i.WorkspaceId == scope.BrainId).ToArray());
            await legacyGrants.SealAsync(cancellationToken);
        }
        _store.State = legacy with { MigrationComplete = true };
        try { await _store.WriteStateAsync(); }
        catch { _store.State = legacy; throw; }
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
