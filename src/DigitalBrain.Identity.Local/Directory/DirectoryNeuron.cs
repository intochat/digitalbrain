using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Contracts;

namespace DigitalBrain.Identity.Local.Directory;

/// <summary>Durable local directory for accounts, principals, brains, memberships, grants and sessions.</summary>
[NeuronAlias(IDirectory.Type), DefaultNeuronType(IDirectory.Type)]
public interface IDirectory
{
    /// <summary>The frozen neuron type name.</summary>
    public const string Type = "identity.directory";

    /// <summary>The single directory neuron key in the system brain.</summary>
    public const string Key = "system/main";

    /// <summary>Reads an account by identifier, or null when missing.</summary>
    Task<Account?> ReadAccount(string accountId);

    /// <summary>Reads the owning account of a brain, or null when missing.</summary>
    Task<Account?> OwnerOfBrain(string brainId);

    /// <summary>Ensures an account exists (idempotent per account identifier).</summary>
    Task EnsureAccount(Account account);

    /// <summary>Creates a brain idempotently per operation identifier.</summary>
    Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind);

    /// <summary>Ensures a brain exists for <paramref name="owner"/>.</summary>
    Task<BrainProvision> EnsureBrain(Member owner);

    /// <summary>Provisions the account and brain for <paramref name="owner"/>.</summary>
    Task<BrainProvision> ProvisionOwner(Member owner);

    /// <summary>Checks whether <paramref name="principalId"/> is registered.</summary>
    Task<bool> IsRegistered(string principalId);

    /// <summary>Registers <paramref name="principalId"/> idempotently.</summary>
    Task<BrainProvision> RegisterPrincipal(string principalId, string displayName);

    /// <summary>Completes registration for <paramref name="principalId"/>, or null when unknown.</summary>
    Task<BrainProvision?> CompleteRegistration(string principalId);

    /// <summary>Lists brains that <paramref name="principalId"/> belongs to.</summary>
    Task<BrainEntry[]> BrainsOf(string principalId);

    /// <summary>Returns the kind of <paramref name="brainScopeId"/> (<see cref="BrainKind.Personal"/> when unknown).</summary>
    Task<BrainKind> KindOf(string brainScopeId);

    /// <summary>Checks whether <paramref name="brainScopeId"/> exists.</summary>
    Task<bool> BrainExists(string brainScopeId);

    /// <summary>Returns the membership of <paramref name="principalId"/> in <paramref name="brainScopeId"/>, or null.</summary>
    Task<Member?> Membership(string brainScopeId, string principalId);

    /// <summary>Ensures <paramref name="member"/> belongs to its brain when called by the brain's owner.</summary>
    Task<Member?> EnsureMember(CapabilityCaller caller, Member member);

    /// <summary>Removes all non-owner memberships for <paramref name="accountId"/> in <paramref name="brainScopeId"/>.</summary>
    Task RemoveMember(CapabilityCaller caller, string brainScopeId, string accountId);

    /// <summary>Removes a non-owner principal from <paramref name="brainScopeId"/>.</summary>
    Task RemovePrincipalMember(CapabilityCaller caller, string brainScopeId, string principalId);

    /// <summary>Authorizes <paramref name="request"/> for <paramref name="caller"/>.</summary>
    Task<CallDecision?> Authorize(CapabilityCaller caller, CallRequest request);

    /// <summary>Lists active grants for <paramref name="brainScopeId"/>.</summary>
    Task<Grant[]> ListGrants(string brainScopeId);

    /// <summary>Records <paramref name="grant"/> when <paramref name="caller"/> is the brain's owner, or null when not.</summary>
    Task<Grant?> GrantAsOwner(CapabilityCaller caller, Grant grant);

    /// <summary>Revokes matching grants in the caller's brain when <paramref name="caller"/> is the owner.</summary>
    Task<bool> RevokeAsOwner(CapabilityCaller caller, string appId, string semanticTypeId, GrantMode mode);

    /// <summary>Issues a session for a current member.</summary>
    Task<SessionIssued> Issue(SessionRequest request);

    /// <summary>Ends a session if present.</summary>
    Task End(string sessionId);

    /// <summary>Validates a session and returns the current member if live, or null.</summary>
    Task<Member?> Validate(string sessionId);
}

/// <summary>Emitted when directory state changes for a scope.</summary>
[Signal("identity.directory-changed")]
public sealed record DirectoryChanged(string Scope) : Signal;

/// <summary>A registered principal record inside <see cref="DirectoryState"/>.</summary>
public sealed record PrincipalRow
{
    /// <summary>The principal identifier.</summary>
    public string PrincipalId { get; init; } = "";

    /// <summary>The principal's default owner membership.</summary>
    public Member? DefaultMembership { get; init; }

    /// <summary>Whether registration has completed.</summary>
    public bool RegistrationComplete { get; init; }
}

/// <summary>A brain record inside <see cref="DirectoryState"/>.</summary>
public sealed record BrainRow
{
    /// <summary>The brain identifier.</summary>
    public string BrainId { get; init; } = "";

    /// <summary>The owning account identifier.</summary>
    public string AccountId { get; init; } = "";

    /// <summary>The brain kind.</summary>
    public BrainKind Kind { get; init; }
}

/// <summary>An active session record inside <see cref="DirectoryState"/>.</summary>
public sealed record SessionRow
{
    /// <summary>The session identifier.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>The member the session was issued for.</summary>
    public required Member Member { get; init; }

    /// <summary>When the session expires.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>An idempotent brain-creation operation record inside <see cref="DirectoryState"/>.</summary>
public sealed record BrainOperation
{
    /// <summary>The account identifier.</summary>
    public string AccountId { get; init; } = "";

    /// <summary>The operation identifier.</summary>
    public string OperationId { get; init; } = "";

    /// <summary>The provisioned owner membership.</summary>
    public required Member Member { get; init; }
}

/// <summary>Persisted state of <see cref="DirectoryNeuron"/>.</summary>
public sealed record DirectoryState
{
    /// <summary>Known accounts.</summary>
    public Account[] Accounts { get; init; } = [];

    /// <summary>Registered principals.</summary>
    public PrincipalRow[] Principals { get; init; } = [];

    /// <summary>Provisioned brains.</summary>
    public BrainRow[] Brains { get; init; } = [];

    /// <summary>Brain memberships.</summary>
    public Member[] Members { get; init; } = [];

    /// <summary>Capability grants.</summary>
    public Grant[] Grants { get; init; } = [];

    /// <summary>Issued sessions.</summary>
    public SessionRow[] Sessions { get; init; } = [];

    /// <summary>Completed brain creation operations.</summary>
    public BrainOperation[] Operations { get; init; } = [];
}

/// <summary>Plain neuron hosting the local identity directory, grants and sessions.</summary>
[Neuron(IDirectory.Type)]
public sealed class DirectoryNeuron(TimeProvider? clock = null) : Neuron<DirectoryState>, IDirectory
{
    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromHours(8);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<Account?> ReadAccount(string accountId)
        => Task.FromResult(AccountOf(State, accountId));

    /// <inheritdoc />
    public Task<Account?> OwnerOfBrain(string brainId)
    {
        var accountId = BrainOf(State, brainId)?.AccountId ?? OwnerMemberOf(State, brainId)?.AccountId;
        return Task.FromResult(accountId is null ? null : AccountOf(State, accountId));
    }

    /// <inheritdoc />
    public async Task EnsureAccount(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.AccountId);
        if (AccountOf(State, account.AccountId) is not null)
        {
            return;
        }

        var next = EnsureAccountState(State, account.AccountId, account.Name, account.OwnerPrincipalId, account.CreatedAt);
        await Commit(next, account.AccountId).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<BrainProvision> CreateBrain(
        string accountId,
        string principalId,
        string operationId,
        string displayName,
        BrainKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (State.Operations.FirstOrDefault(op => op.OperationId == operationId) is { } existing)
        {
            return new BrainProvision(existing.Member, false);
        }

        var next = EnsureAccountState(State, accountId, displayName, principalId, default);
        var brainId = $"brain-{Guid.NewGuid():N}";
        var owner = new Member
        {
            PrincipalId = principalId,
            AccountId = accountId,
            BrainId = brainId,
            Role = MemberRole.Owner,
            DisplayName = displayName,
            JoinedAt = _clock.GetUtcNow(),
        };
        next = next with
        {
            Brains = [.. next.Brains, new BrainRow { BrainId = brainId, AccountId = accountId, Kind = kind }],
            Members = [.. next.Members, owner],
            Operations = [.. next.Operations, new BrainOperation { AccountId = accountId, OperationId = operationId, Member = owner }],
        };
        await Commit(next, brainId).ConfigureAwait(true);
        return new BrainProvision(owner, true);
    }

    /// <inheritdoc />
    public async Task<BrainProvision> EnsureBrain(Member owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var next = EnsureAccountState(State, owner.AccountId, owner.DisplayName, owner.PrincipalId, owner.JoinedAt);
        var (provisioned, provision) = EnsureBrainState(next, owner);
        await Commit(provisioned, provision.Member.BrainId).ConfigureAwait(true);
        return provision;
    }

    /// <inheritdoc />
    public async Task<BrainProvision> ProvisionOwner(Member owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var next = EnsureAccountState(State, owner.AccountId, owner.DisplayName, owner.PrincipalId, owner.JoinedAt);
        var (provisioned, provision) = EnsureBrainState(next, owner);
        await Commit(provisioned, provision.Member.BrainId).ConfigureAwait(true);
        return provision;
    }

    /// <inheritdoc />
    public Task<bool> IsRegistered(string principalId)
        => Task.FromResult(PrincipalOf(State, principalId)?.RegistrationComplete == true);

    /// <inheritdoc />
    public async Task<BrainProvision> RegisterPrincipal(string principalId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        if (PrincipalOf(State, principalId) is { RegistrationComplete: true, DefaultMembership: { } existing })
        {
            return new BrainProvision(existing, false);
        }

        var owner = new Member
        {
            PrincipalId = principalId,
            AccountId = $"account-{principalId}",
            BrainId = $"brain-{principalId}",
            Role = MemberRole.Owner,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? principalId : displayName,
            JoinedAt = _clock.GetUtcNow(),
        };
        var next = EnsureAccountState(State, owner.AccountId, owner.DisplayName, principalId, owner.JoinedAt);
        var (provisioned, provision) = EnsureBrainState(next, owner);
        var row = new PrincipalRow
        {
            PrincipalId = principalId,
            DefaultMembership = provision.Member,
            RegistrationComplete = true,
        };
        var completed = provisioned with
        {
            Principals = [.. provisioned.Principals.Where(p => p.PrincipalId != principalId), row],
        };
        await Commit(completed, provision.Member.BrainId).ConfigureAwait(true);
        return provision;
    }

    /// <inheritdoc />
    public Task<BrainProvision?> CompleteRegistration(string principalId)
        => Task.FromResult(
            PrincipalOf(State, principalId) is { RegistrationComplete: true, DefaultMembership: { } member }
                ? new BrainProvision(member, false)
                : null);

    /// <inheritdoc />
    public Task<BrainEntry[]> BrainsOf(string principalId)
    {
        var entries = State.Members
            .Where(m => m.PrincipalId == principalId)
            .Select(m => m.BrainId)
            .Distinct(StringComparer.Ordinal)
            .Select(brainId => new BrainEntry(brainId, BrainOf(State, brainId)?.Kind ?? BrainKind.Personal))
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(entries);
    }

    /// <inheritdoc />
    public Task<BrainKind> KindOf(string brainScopeId)
        => Task.FromResult(BrainOf(State, brainScopeId)?.Kind ?? BrainKind.Personal);

    /// <inheritdoc />
    public Task<bool> BrainExists(string brainScopeId)
        => Task.FromResult(BrainOf(State, brainScopeId) is not null);

    /// <inheritdoc />
    public Task<Member?> Membership(string brainScopeId, string principalId)
        => Task.FromResult(State.Members.FirstOrDefault(m => m.BrainId == brainScopeId && m.PrincipalId == principalId));

    /// <inheritdoc />
    public async Task<Member?> EnsureMember(CapabilityCaller caller, Member member)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(member);
        RequireOwner(State, caller, member.BrainId);
        if (State.Members.FirstOrDefault(m => m.BrainId == member.BrainId && m.PrincipalId == member.PrincipalId) is { } existing)
        {
            return existing;
        }

        if (member.Role == MemberRole.Owner)
        {
            throw new InvalidOperationException($"Brain {member.BrainId} already has an owner.");
        }

        var added = member with { JoinedAt = member.JoinedAt == default ? _clock.GetUtcNow() : member.JoinedAt };
        await Commit(State with { Members = [.. State.Members, added] }, member.BrainId).ConfigureAwait(true);
        return added;
    }

    /// <inheritdoc />
    public async Task RemoveMember(CapabilityCaller caller, string brainScopeId, string accountId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        RequireOwner(State, caller, brainScopeId);
        if (State.Members.Any(m => m.BrainId == brainScopeId && m.AccountId == accountId && m.Role == MemberRole.Owner))
        {
            throw new InvalidOperationException($"Owner of {brainScopeId} cannot be removed.");
        }

        var remaining = State.Members.Where(m => !(m.BrainId == brainScopeId && m.AccountId == accountId)).ToArray();
        if (remaining.Length != State.Members.Length)
        {
            await Commit(State with { Members = remaining }, brainScopeId).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task RemovePrincipalMember(CapabilityCaller caller, string brainScopeId, string principalId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(brainScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        RequireOwner(State, caller, brainScopeId);
        var target = State.Members.FirstOrDefault(m => m.BrainId == brainScopeId && m.PrincipalId == principalId);
        if (target is null)
        {
            return;
        }

        if (target.Role == MemberRole.Owner)
        {
            throw new InvalidOperationException($"Owner of {brainScopeId} cannot be removed.");
        }

        var remaining = State.Members.Where(m => !(m.BrainId == brainScopeId && m.PrincipalId == principalId)).ToArray();
        await Commit(State with { Members = remaining }, brainScopeId).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<CallDecision?> Authorize(CapabilityCaller caller, CallRequest request)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(caller.AppId))
        {
            return CallDecision.Allow();
        }

        var consumesOnce = false;
        var consumedIndices = new HashSet<int>();
        foreach (var type in request.SemanticTypeIds)
        {
            var matchedIndex = -1;
            for (var i = 0; i < State.Grants.Length; i++)
            {
                if (State.Grants[i].Covers(caller.AppId, type, caller.BrainId, caller.ConversationId))
                {
                    matchedIndex = i;
                    break;
                }
            }

            if (matchedIndex < 0)
            {
                return CallDecision.Deny(CallDenial.MissingGrant, $"app {caller.AppId} has no grant for {type}");
            }

            if (State.Grants[matchedIndex].Mode == GrantMode.Once)
            {
                consumesOnce = true;
                consumedIndices.Add(matchedIndex);
            }
        }

        if (consumesOnce)
        {
            var nextGrants = (Grant[])State.Grants.Clone();
            foreach (var index in consumedIndices)
            {
                nextGrants[index] = nextGrants[index] with { Revoked = true };
            }

            await Commit(State with { Grants = nextGrants }, caller.BrainId).ConfigureAwait(true);
        }

        return CallDecision.Allow() with { ConsumesOnce = consumesOnce };
    }

    /// <inheritdoc />
    public Task<Grant[]> ListGrants(string brainScopeId)
        => Task.FromResult(State.Grants.Where(g => g.WorkspaceId == brainScopeId && !g.Revoked).ToArray());

    /// <inheritdoc />
    public async Task<Grant?> GrantAsOwner(CapabilityCaller caller, Grant grant)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(grant);
        var brainId = string.IsNullOrWhiteSpace(grant.WorkspaceId) ? caller.BrainId : grant.WorkspaceId;
        if (!IsOwner(State, caller, brainId))
        {
            return null;
        }

        var stored = grant with { WorkspaceId = brainId, GrantedAt = _clock.GetUtcNow(), Revoked = false };
        var nextGrants = State.Grants
            .Where(existing => !(existing.AppId == stored.AppId
                && existing.SemanticTypeId == stored.SemanticTypeId
                && existing.Mode == stored.Mode
                && existing.WorkspaceId == stored.WorkspaceId
                && existing.ConversationId == stored.ConversationId))
            .Append(stored)
            .ToArray();
        await Commit(State with { Grants = nextGrants }, brainId).ConfigureAwait(true);
        return stored;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsOwner(CapabilityCaller caller, string appId, string semanticTypeId, GrantMode mode)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!IsOwner(State, caller, caller.BrainId))
        {
            return false;
        }

        var changed = false;
        var nextGrants = new Grant[State.Grants.Length];
        for (var i = 0; i < State.Grants.Length; i++)
        {
            var grant = State.Grants[i];
            if (grant.WorkspaceId == caller.BrainId
                && grant.AppId == appId
                && grant.SemanticTypeId == semanticTypeId
                && grant.Mode == mode
                && !grant.Revoked)
            {
                nextGrants[i] = grant with { Revoked = true };
                changed = true;
            }
            else
            {
                nextGrants[i] = grant;
            }
        }

        if (changed)
        {
            await Commit(State with { Grants = nextGrants }, caller.BrainId).ConfigureAwait(true);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<SessionIssued> Issue(SessionRequest request)
    {
        SessionClaims.Validate(request);
        var member = request.Member;
        var current = State.Members.FirstOrDefault(m => m.BrainId == member.BrainId && m.PrincipalId == member.PrincipalId);
        if (current is null || current.AccountId != member.AccountId)
        {
            throw new InvalidOperationException($"{member.PrincipalId} is not a member of {member.BrainId}; no session is issued.");
        }

        var issued = new SessionIssued(
            $"session-{Guid.NewGuid():N}",
            _clock.GetUtcNow() + (request.Lifetime ?? DefaultSessionLifetime));
        var row = new SessionRow
        {
            SessionId = issued.SessionId,
            Member = current,
            ExpiresAt = issued.ExpiresAt,
        };
        await Commit(State with { Sessions = [.. State.Sessions, row] }, current.BrainId).ConfigureAwait(true);
        return issued;
    }

    /// <inheritdoc />
    public async Task End(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        var remaining = State.Sessions.Where(s => s.SessionId != sessionId).ToArray();
        if (remaining.Length != State.Sessions.Length)
        {
            await Commit(State with { Sessions = remaining }, sessionId).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public Task<Member?> Validate(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        var session = State.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (session is null || session.ExpiresAt <= _clock.GetUtcNow())
        {
            return Task.FromResult<Member?>(null);
        }

        var current = State.Members.FirstOrDefault(
            m => m.BrainId == session.Member.BrainId && m.PrincipalId == session.Member.PrincipalId);
        return Task.FromResult(current);
    }

    private DirectoryState EnsureAccountState(
        DirectoryState state,
        string accountId,
        string name,
        string ownerPrincipalId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (AccountOf(state, accountId) is not null)
        {
            return state;
        }

        var account = new Account
        {
            AccountId = accountId,
            Name = name,
            OwnerPrincipalId = ownerPrincipalId,
            CreatedAt = createdAt == default ? _clock.GetUtcNow() : createdAt,
        };
        return state with { Accounts = [.. state.Accounts, account] };
    }

    private (DirectoryState Next, BrainProvision Provision) EnsureBrainState(DirectoryState state, Member owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.BrainId);
        if (OwnerMemberOf(state, owner.BrainId) is { } existing)
        {
            return (state, new BrainProvision(existing, false));
        }

        var stamped = owner with
        {
            Role = MemberRole.Owner,
            JoinedAt = owner.JoinedAt == default ? _clock.GetUtcNow() : owner.JoinedAt,
        };
        var brains = BrainOf(state, owner.BrainId) is null
            ? [.. state.Brains, new BrainRow { BrainId = owner.BrainId, AccountId = owner.AccountId, Kind = BrainKind.Personal }]
            : state.Brains;
        var next = state with
        {
            Brains = brains,
            Members = [.. state.Members, stamped],
        };
        return (next, new BrainProvision(stamped, true));
    }

    private async Task Commit(DirectoryState next, string scope)
    {
        if (ReferenceEquals(next, State))
        {
            return;
        }

        await Save(next, new DirectoryChanged(scope)).ConfigureAwait(true);
    }

    private static void RequireOwner(DirectoryState state, CapabilityCaller caller, string brainId)
    {
        if (!IsOwner(state, caller, brainId))
        {
            throw new UnauthorizedAccessException($"{caller.PrincipalId} is not the owner of {brainId}.");
        }
    }

    private static bool IsOwner(DirectoryState state, CapabilityCaller caller, string brainId)
    {
        if (!string.IsNullOrWhiteSpace(caller.AppId))
        {
            return false;
        }

        var owner = OwnerMemberOf(state, brainId);
        return owner is not null && owner.PrincipalId == caller.PrincipalId;
    }

    private static Account? AccountOf(DirectoryState state, string accountId)
        => state.Accounts.FirstOrDefault(a => a.AccountId == accountId);

    private static PrincipalRow? PrincipalOf(DirectoryState state, string principalId)
        => state.Principals.FirstOrDefault(p => p.PrincipalId == principalId);

    private static BrainRow? BrainOf(DirectoryState state, string brainId)
        => state.Brains.FirstOrDefault(b => b.BrainId == brainId);

    private static Member? OwnerMemberOf(DirectoryState state, string brainId)
        => state.Members.FirstOrDefault(m => m.BrainId == brainId && m.Role == MemberRole.Owner);
}
