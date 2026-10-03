using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Grants;
using Orleans;
using Orleans.Runtime;
using Microsoft.Extensions.Options;
using DigitalBrain.Platform.Identity.Configuration;

namespace DigitalBrain.Platform.Identity.Authority;

[GenerateSerializer, Alias("identity.brain-access-state.v2")]
internal sealed record BrainAuthorityState
{
    [Id(0)] public string? AccountId { get; init; }
    [Id(1)] public string? BrainId { get; init; }
    [Id(2)] public Dictionary<string, Member> Members { get; init; } = [];
    [Id(3)] public Grant[] Grants { get; init; } = [];
    [Id(4)] public Invitation[] Invitations { get; init; } = [];
    [Id(5)] public bool Imported { get; init; }
    [Id(6)] public bool OwnerProvisioned { get; init; }
    [Id(7)] public AppGrantMigrationReceipt[] AppGrantMigrations { get; init; } = [];
}

[GenerateSerializer]
internal sealed record AppGrantMigrationReceipt([property: Id(0)] string FileId, [property: Id(1)] string AppId);

// Deliberately non-reentrant: a Once grant is checked and persisted in one turn.
[GrainType("identity.brain-access.v2")]
internal sealed class BrainAuthorityGrain(
    [PersistentState("access", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BrainAuthorityState> storage,
    TimeProvider clock, IOptions<AuthOptions> auth) : IdentityStateGrain<BrainAuthorityState>(storage), IBrainAuthority, IAppGrantMigration, IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.InterfaceMethod.DeclaringType == typeof(IAppGrantMigration))
        {
            var source = context.SourceId;
            var caller = CallerContextStamper.Require();
            if (!CallerContextStamper.IsTrusted(caller) || source?.Type.ToString() != "apps.app"
                || source?.Key.ToString() != (string)context.Request.GetArgument(0)!
                || BrainScope.Create(caller.AccountId, caller.BrainId).Id != this.GetPrimaryKeyString())
            { throw new UnauthorizedAccessException("Only the originating Apps grain may migrate grants in its current brain."); }
        }
        await context.Invoke();
    }

    public async Task Migrate(string appId, string[] legacyFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(legacyFiles);
        if (legacyFiles.Any(string.IsNullOrWhiteSpace)) { throw new ArgumentException("Legacy file IDs must not be empty.", nameof(legacyFiles)); }
        var files = legacyFiles.Where(file => file != appId).Distinct(StringComparer.Ordinal).ToArray();
        if (State.AppGrantMigrations.Any(receipt => files.Contains(receipt.FileId, StringComparer.Ordinal) && receipt.AppId != appId))
        { throw new UnauthorizedAccessException("A legacy file's grants already belong to another app."); }
        var pending = files.Except(State.AppGrantMigrations.Select(receipt => receipt.FileId), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (pending.Count == 0) { return; }
        var caller = CallerContextStamper.Require();
        await Initialize(caller.AccountId, caller.BrainId);
        // Reuse the authority's stored grants, not Grant(): migration retains dates, chat scopes,
        // and revoked values. A revoked collision wins; an existing stable grant takes precedence.
        var migrated = State.Grants.OrderBy(grant => pending.Contains(grant.AppId) ? 1 : 0)
            .Select(grant => pending.Contains(grant.AppId) ? grant with { AppId = appId } : grant)
            .Where(grant => grant.AppId == appId)
            .GroupBy(grant => (grant.AppId, grant.SemanticTypeId, grant.Mode, Conversation: grant.Mode == GrantMode.ThisChat ? grant.ConversationId : null))
            .Select(group => group.FirstOrDefault(grant => grant.Revoked) ?? group.First()).ToArray();
        // One atomic save records each retired file with the move. Repeating a deployment after
        // a stable grant is revoked or spent cannot import that file again.
        await Persist(State with
        {
            Grants = [.. State.Grants.Where(grant => grant.AppId != appId && !pending.Contains(grant.AppId)), .. migrated],
            AppGrantMigrations = [.. State.AppGrantMigrations, .. pending.Select(file => new AppGrantMigrationReceipt(file, appId))]
        });
    }
    public async Task Initialize(string accountId, string brainId)
    {
        if (BrainScope.Create(accountId, brainId).Id != this.GetPrimaryKeyString()) { throw new InvalidOperationException("Brain authority key mismatch."); }
        if (State.AccountId is not null && (State.AccountId != accountId || State.BrainId != brainId))
        { throw new InvalidOperationException("Brain authority scope conflict."); }
        if (State.AccountId is null) { await Persist(State with { AccountId = accountId, BrainId = brainId }); }
    }
    public Task<Member?> Membership(string principalId) => Task.FromResult(State.Members.GetValueOrDefault(principalId));
    public async Task ProvisionOwner(Member owner)
    {
        await Initialize(owner.AccountId, owner.BrainId);
        if (State.OwnerProvisioned) { return; }
        if (owner.Role != MemberRole.Owner) { throw new ArgumentException("An owner is required."); }
        await Persist(State with { Members = new(State.Members) { [owner.PrincipalId] = owner }, OwnerProvisioned = true });
    }
    public async Task EnsureMember(Member member)
    {
        await Initialize(member.AccountId, member.BrainId);
        if (State.Members.TryGetValue(member.PrincipalId, out var existing))
        {
            if (existing.Role != member.Role) { throw new InvalidOperationException("Membership role conflict."); }
            return;
        }
        await Persist(State with { Members = new(State.Members) { [member.PrincipalId] = member } });
    }
    public Task RemoveMember(string principalId)
    {
        var members = new Dictionary<string, Member>(State.Members);
        members.Remove(principalId);
        return Persist(State with { Members = members });
    }
    public async Task<CallDecision?> Authorize(CallRequest request)
    {
        var caller = request.Caller;
        if (State.AccountId != caller.AccountId || State.BrainId != caller.BrainId)
        { return CallDecision.Deny(CallDenial.OutsideWorkspace, "The target scope does not match this brain."); }
        var decision = GrantRules.Evaluate(request, State.Members.GetValueOrDefault(caller.PrincipalId), State.Grants);
        if (decision is { Allowed: true })
        {
            var consumed = GrantRules.OnceToConsume(request, State.Grants);
            if (consumed.Count > 0) { await Persist(State with { Grants = [.. State.Grants.Except(consumed)] }); }
        }
        return decision;
    }
    public async Task<Grant> GrantAsOwner(CallerContext caller, Grant grant)
    {
        await RequireOwner(caller);
        return await Grant(grant);
    }
    public async Task RevokeAsOwner(CallerContext caller, string appId, string semanticTypeId, GrantMode mode)
    {
        await RequireOwner(caller);
        await Revoke(appId, semanticTypeId, mode);
    }
    private async Task RequireOwner(CallerContext caller)
    {
        if (!CallerContextStamper.IsTrusted(caller) || caller.Kind != CallerKind.User
            || BrainScope.Create(caller.AccountId, caller.BrainId).Id != this.GetPrimaryKeyString())
        { throw new UnauthorizedAccessException("Only the current brain owner can manage grants."); }
        if (auth.Value.Posture == IdentityPosture.Open && caller.PrincipalId == "owner" && caller.AccountId == "owner")
        { await Initialize(caller.AccountId, caller.BrainId); return; }
        if (State.Members.GetValueOrDefault(caller.PrincipalId)?.Role != MemberRole.Owner)
        { throw new UnauthorizedAccessException("Only the current brain owner can manage grants."); }
    }
    public async Task<Grant> Grant(Grant grant)
    {
        if (State.BrainId is null) { throw new InvalidOperationException("Initialize the brain authority before granting access."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.AppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.SemanticTypeId);
        var stored = grant with { WorkspaceId = State.BrainId, GrantedAt = clock.GetUtcNow() };
        await Persist(State with
        {
            Grants = [.. State.Grants.Where(g => g.AppId != stored.AppId || g.SemanticTypeId != stored.SemanticTypeId
            || g.Mode != stored.Mode || (g.Mode == GrantMode.ThisChat && g.ConversationId != stored.ConversationId)), stored]
        });
        return stored;
    }
    public Task Revoke(string appId, string semanticTypeId, GrantMode mode)
        => Persist(State with { Grants = [.. State.Grants.Where(g => g.AppId != appId || g.SemanticTypeId != semanticTypeId || g.Mode != mode)] });
    public Task<Grant[]> ListGrants() => Task.FromResult(State.Grants.Where(g => !g.Revoked).ToArray());
    public async Task Import(Member[] members, Grant[] grants, Invitation[] invitations)
    {
        if (State.Imported) { return; }
        if (State.AccountId is null || members.Any(m => m.AccountId != State.AccountId || m.BrainId != State.BrainId)
            || grants.Any(g => g.WorkspaceId != State.BrainId) || invitations.Any(i => i.AccountId != State.AccountId || i.WorkspaceId != State.BrainId))
        { throw new InvalidOperationException("Imported authorization state has a conflicting scope."); }
        if (State.Members.Count != 0 || State.Grants.Length != 0) { throw new InvalidOperationException("Import requires an empty authority."); }
        await Persist(State with { Members = members.ToDictionary(m => m.PrincipalId), Grants = grants, Invitations = invitations, Imported = true, OwnerProvisioned = members.Any(m => m.Role == MemberRole.Owner) });
    }
}
