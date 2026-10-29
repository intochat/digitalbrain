using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Testing.Conformance.Identity;

/// <summary>
/// The identity@1 reference implementation (both halves, <see cref="IIdentity"/> and <see cref="IGrants"/>) in memory. It
/// passes <see cref="IdentityConformance{TIdentity}"/>. Semantics follow the os identity authority: provisioning is idempotent
/// (per principal, per brain id, per create operation id), the owner cannot be removed, an unknown brain is
/// <see cref="BrainKind.Personal"/>, grants are scoped to the brain (<see cref="Grant.WorkspaceId"/> is the brain id) and only
/// the brain's owner grants or revokes. Authorize allows user callers and requires a covering grant for every semantic type
/// an app caller touches. Sessions (<see cref="ISessions"/>) are privileged: only a caller whose app id
/// <see cref="Declarations"/> grants the member may issue, end or validate (fail closed; none by default). Issuing requires a
/// current membership; a session lives <see cref="DefaultSessionLifetime"/> unless the request says otherwise.
/// </summary>
/// <param name="callers">Supplies the runtime caller; a local owner when null.</param>
/// <param name="time">The clock for created, joined and granted times.</param>
public class InMemoryIdentity(AmbientCallerScope? callers = null, TimeProvider? time = null) : IIdentity, IGrants, ISessions, IIdentityProvider
{
    /// <summary>The lifetime of a session whose request names none.</summary>
    public static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromHours(8);

    private readonly AmbientCallerScope _callers = callers ?? new AmbientCallerScope();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Account> _accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BrainKind> _brains = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Member>> _members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Member> _registered = new(StringComparer.Ordinal);
    private readonly List<Grant> _grants = [];
    private readonly List<ISignInHandler> _handlers = [];
    private readonly Dictionary<string, (Member Member, DateTimeOffset ExpiresAt)> _sessions = new(StringComparer.Ordinal);

    /// <summary>The module declarations the privileged session members check; <see cref="ModuleDeclarations.None"/> by default.</summary>
    public ModuleDeclarations Declarations { get; init; } = ModuleDeclarations.None;

    /// <summary>What <see cref="Describe"/> answers; a Secured <c>memory</c> implementation without an account surface by default.</summary>
    public IdentityProviderDescriptor Descriptor { get; init; } = new() { ProviderId = "memory", Posture = IdentityPosture.Secured };

    /// <summary>Adds a handler <see cref="SignIn"/> invokes after the session would be issued.</summary>
    public void AddSignInHandler(ISignInHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate) { _handlers.Add(handler); }
    }

    /// <summary>
    /// Simulates a host sign-in: registers <paramref name="principalId"/> if needed and invokes every sign-in handler with the
    /// principal's owner membership. A failing handler is swallowed (it never fails sign-in).
    /// </summary>
    public async Task<Member> SignIn(string principalId, CancellationToken cancellationToken)
    {
        var provision = await RegisterPrincipal(principalId, principalId, cancellationToken).ConfigureAwait(false);
        ISignInHandler[] handlers;
        lock (_gate) { handlers = [.. _handlers]; }
        foreach (var handler in handlers)
        {
            try
            {
                await handler.SignedIn(provision.Member, cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A failing handler never fails sign-in (identity@1 contract).
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
            }
        }

        return provision.Member;
    }

    // ----- accounts -----

    /// <inheritdoc />
    public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(ReadAccount));
        lock (_gate) { return Task.FromResult(_accounts.GetValueOrDefault(accountId)); }
    }

    /// <inheritdoc />
    public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(OwnerOfBrain));
        lock (_gate)
        {
            var owner = OwnerLocked(brainId);
            return Task.FromResult(owner is null ? null : _accounts.GetValueOrDefault(owner.AccountId));
        }
    }

    /// <inheritdoc />
    public Task EnsureAccount(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        OnCall(nameof(EnsureAccount));
        lock (_gate) { EnsureAccountLocked(account.AccountId, account.Name, account.OwnerPrincipalId, account.CreatedAt); }
        return Task.CompletedTask;
    }

    // ----- brains -----

    /// <inheritdoc />
    public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        OnCall(nameof(CreateBrain));
        lock (_gate)
        {
            if (_operations.TryGetValue(operationId, out var existing))
            {
                return Task.FromResult(new BrainProvision(OwnerLocked(existing)!, false));
            }

            EnsureAccountLocked(accountId, displayName, principalId, default);
            var brainId = $"brain-{Guid.NewGuid():N}";
            _operations[operationId] = brainId;
            var owner = AddBrainLocked(brainId, kind, new Member
            {
                PrincipalId = principalId,
                AccountId = accountId,
                BrainId = brainId,
                Role = MemberRole.Owner,
                DisplayName = displayName,
            });
            return Task.FromResult(new BrainProvision(owner, true));
        }
    }

    /// <inheritdoc />
    public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        OnCall(nameof(EnsureBrain));
        lock (_gate) { return Task.FromResult(EnsureBrainLocked(owner)); }
    }

    /// <inheritdoc />
    public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        OnCall(nameof(ProvisionOwner));
        lock (_gate)
        {
            EnsureAccountLocked(owner.AccountId, owner.DisplayName, owner.PrincipalId, default);
            return Task.FromResult(EnsureBrainLocked(owner));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(BrainsOf));
        lock (_gate)
        {
            IReadOnlyList<BrainEntry> brains = [.. _members
                .Where(pair => pair.Value.Exists(member => member.PrincipalId == principalId))
                .Select(pair => new BrainEntry(pair.Key, _brains[pair.Key]))
                .OrderBy(entry => entry.Id, StringComparer.Ordinal)];
            return Task.FromResult(brains);
        }
    }

    /// <inheritdoc />
    public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(KindOf));
        lock (_gate) { return Task.FromResult(_brains.GetValueOrDefault(brainScopeId, BrainKind.Personal)); }
    }

    /// <inheritdoc />
    public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(BrainExists));
        lock (_gate) { return Task.FromResult(_brains.ContainsKey(brainScopeId)); }
    }

    // ----- principals -----

    /// <inheritdoc />
    public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(IsRegistered));
        lock (_gate) { return Task.FromResult(_registered.ContainsKey(principalId)); }
    }

    /// <inheritdoc />
    public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        OnCall(nameof(RegisterPrincipal));
        lock (_gate)
        {
            if (_registered.TryGetValue(principalId, out var existing)) { return Task.FromResult(new BrainProvision(existing, false)); }
            var owner = new Member
            {
                PrincipalId = principalId,
                AccountId = $"account-{principalId}",
                BrainId = $"brain-{principalId}",
                Role = MemberRole.Owner,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? principalId : displayName,
            };
            EnsureAccountLocked(owner.AccountId, owner.DisplayName, principalId, default);
            var provision = EnsureBrainLocked(owner);
            _registered[principalId] = provision.Member;
            return Task.FromResult(provision);
        }
    }

    /// <inheritdoc />
    public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(CompleteRegistration));
        lock (_gate)
        {
            return Task.FromResult(_registered.TryGetValue(principalId, out var member) ? new BrainProvision(member, false) : null);
        }
    }

    // ----- membership -----

    /// <inheritdoc />
    public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(Membership));
        lock (_gate)
        {
            return Task.FromResult(_members.TryGetValue(brainScopeId, out var members) ? members.Find(member => member.PrincipalId == principalId) : null);
        }
    }

    /// <inheritdoc />
    public Task<Member?> EnsureMember(Member member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        OnCall(nameof(EnsureMember));
        lock (_gate)
        {
            if (!_members.TryGetValue(member.BrainId, out var members)) { return Task.FromResult<Member?>(null); }
            var existing = members.Find(candidate => candidate.PrincipalId == member.PrincipalId);
            if (existing is not null) { return Task.FromResult<Member?>(existing); }
            if (member.Role == MemberRole.Owner) { throw new InvalidOperationException($"brain {member.BrainId} already has an owner"); }
            var added = member with { JoinedAt = member.JoinedAt == default ? _time.GetUtcNow() : member.JoinedAt };
            members.Add(added);
            return Task.FromResult<Member?>(added);
        }
    }

    /// <inheritdoc />
    public Task RemoveMember(string brainScopeId, string accountId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(RemoveMember));
        lock (_gate)
        {
            if (_members.TryGetValue(brainScopeId, out var members))
            {
                members.RemoveAll(member => member.AccountId == accountId && member.Role != MemberRole.Owner);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemovePrincipalMember(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(RemovePrincipalMember));
        lock (_gate)
        {
            if (_members.TryGetValue(brainScopeId, out var members))
            {
                members.RemoveAll(member => member.PrincipalId == principalId && member.Role != MemberRole.Owner);
            }
        }

        return Task.CompletedTask;
    }

    // ----- grants -----

    /// <inheritdoc />
    public Task<CallDecision?> Authorize(CallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OnCall(nameof(Authorize));
        var caller = _callers.Current;
        if (string.IsNullOrWhiteSpace(caller.AppId)) { return Task.FromResult<CallDecision?>(CallDecision.Allow()); }
        lock (_gate)
        {
            var consumesOnce = false;
            foreach (var type in request.SemanticTypeIds)
            {
                var grant = _grants.Find(candidate => candidate.Covers(caller.AppId, type, caller.BrainId, caller.ConversationId));
                if (grant is null)
                {
                    return Task.FromResult<CallDecision?>(CallDecision.Deny(CallDenial.MissingGrant, $"app {caller.AppId} has no grant for {type}"));
                }

                consumesOnce |= grant.Mode == GrantMode.Once;
            }

            return Task.FromResult<CallDecision?>(CallDecision.Allow() with { ConsumesOnce = consumesOnce });
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(ListGrants));
        lock (_gate)
        {
            IReadOnlyList<Grant> grants = [.. _grants.Where(grant => grant.WorkspaceId == brainScopeId && !grant.Revoked)];
            return Task.FromResult(grants);
        }
    }

    /// <inheritdoc />
    public Task<Grant> GrantAsOwner(Grant grant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        OnCall(nameof(GrantAsOwner));
        var caller = _callers.Current;
        lock (_gate)
        {
            RequireOwnerLocked(caller, grant.WorkspaceId);
            var stored = grant with { GrantedAt = _time.GetUtcNow(), Revoked = false };
            _grants.RemoveAll(existing => existing.AppId == stored.AppId && existing.SemanticTypeId == stored.SemanticTypeId
                && existing.Mode == stored.Mode && existing.WorkspaceId == stored.WorkspaceId && existing.ConversationId == stored.ConversationId);
            _grants.Add(stored);
            return Task.FromResult(stored);
        }
    }

    /// <inheritdoc />
    public Task RevokeAsOwner(string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(RevokeAsOwner));
        var caller = _callers.Current;
        lock (_gate)
        {
            RequireOwnerLocked(caller, caller.BrainId);
            for (var i = 0; i < _grants.Count; i++)
            {
                var grant = _grants[i];
                if (grant.WorkspaceId == caller.BrainId && grant.AppId == appId && grant.SemanticTypeId == semanticTypeId && grant.Mode == mode)
                {
                    _grants[i] = grant with { Revoked = true };
                }
            }
        }

        return Task.CompletedTask;
    }

    // ----- sessions -----

    /// <inheritdoc />
    public Task<SessionIssued> Issue(SessionRequest request, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(Issue));
        Declarations.RequirePrivileged(_callers.Current.AppId, Capability.Identity.V1, nameof(Issue));
        return Task.FromResult(IssueSession(request));
    }

    /// <inheritdoc />
    public Task End(string sessionId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(End));
        Declarations.RequirePrivileged(_callers.Current.AppId, Capability.Identity.V1, nameof(End));
        EndSession(sessionId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Member?> Validate(string sessionId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(Validate));
        Declarations.RequirePrivileged(_callers.Current.AppId, Capability.Identity.V1, nameof(Validate));
        return Task.FromResult(ValidateSession(sessionId));
    }

    /// <inheritdoc />
    public IdentityProviderDescriptor Describe()
    {
        OnCall(nameof(Describe));
        return Descriptor;
    }

    // The unchecked session operations; the privilege check belongs to the caller (ISessions above, or the runtime over the port).
    internal SessionIssued IssueSession(SessionRequest request)
    {
        SessionClaims.Validate(request);
        var member = request.Member;
        lock (_gate)
        {
            var current = _members.TryGetValue(member.BrainId, out var members) ? members.Find(candidate => candidate.PrincipalId == member.PrincipalId) : null;
            if (current is null || current.AccountId != member.AccountId)
            {
                throw new InvalidOperationException($"{member.PrincipalId} is not a member of {member.BrainId}; no session is issued.");
            }

            var issued = new SessionIssued($"session-{Guid.NewGuid():N}", _time.GetUtcNow() + (request.Lifetime ?? DefaultSessionLifetime));
            _sessions[issued.SessionId] = (current, issued.ExpiresAt);
            return issued;
        }
    }

    internal void EndSession(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_gate) { _sessions.Remove(sessionId); }
    }

    internal Member? ValidateSession(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) { return null; }
            if (session.ExpiresAt <= _time.GetUtcNow())
            {
                _sessions.Remove(sessionId);
                return null;
            }

            var member = session.Member;
            return _members.TryGetValue(member.BrainId, out var members) ? members.Find(candidate => candidate.PrincipalId == member.PrincipalId) : null;
        }
    }

    /// <summary>Called on entry of each identity@1 member with the member name.</summary>
    protected virtual void OnCall(string member)
    {
    }

    private void RequireOwnerLocked(CapabilityCaller caller, string brainId)
    {
        var owner = OwnerLocked(brainId);
        if (owner is null || owner.PrincipalId != caller.PrincipalId || !string.IsNullOrWhiteSpace(caller.AppId))
        {
            throw new UnauthorizedAccessException($"{caller.PrincipalId} is not the owner of {brainId}");
        }
    }

    private Member? OwnerLocked(string brainId) =>
        _members.TryGetValue(brainId, out var members) ? members.Find(member => member.Role == MemberRole.Owner) : null;

    private void EnsureAccountLocked(string accountId, string name, string ownerPrincipalId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (_accounts.ContainsKey(accountId)) { return; }
        _accounts[accountId] = new Account
        {
            AccountId = accountId,
            Name = name,
            OwnerPrincipalId = ownerPrincipalId,
            CreatedAt = createdAt == default ? _time.GetUtcNow() : createdAt,
        };
    }

    private BrainProvision EnsureBrainLocked(Member owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.BrainId);
        var existing = OwnerLocked(owner.BrainId);
        if (existing is not null) { return new BrainProvision(existing, false); }
        return new BrainProvision(AddBrainLocked(owner.BrainId, BrainKind.Personal, owner with { Role = MemberRole.Owner }), true);
    }

    private Member AddBrainLocked(string brainId, BrainKind kind, Member owner)
    {
        var stamped = owner with { JoinedAt = owner.JoinedAt == default ? _time.GetUtcNow() : owner.JoinedAt };
        _brains[brainId] = kind;
        _members[brainId] = [stamped];
        return stamped;
    }
}
