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
}

// Deliberately non-reentrant: a Once grant is checked and persisted in one turn.
[GrainType("identity.brain-access.v2")]
internal sealed class BrainAuthorityGrain(
    [PersistentState("access", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BrainAuthorityState> storage,
    TimeProvider clock, IOptions<AuthOptions> auth) : IdentityStateGrain<BrainAuthorityState>(storage), IBrainAuthority
{
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
        await Persist(State with { Grants = [.. State.Grants.Where(g => g.AppId != stored.AppId || g.SemanticTypeId != stored.SemanticTypeId
            || g.Mode != stored.Mode || (g.Mode == GrantMode.ThisChat && g.ConversationId != stored.ConversationId)), stored] });
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
