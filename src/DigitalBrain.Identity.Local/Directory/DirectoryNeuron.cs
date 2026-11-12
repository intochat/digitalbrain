namespace DigitalBrain.Identity.Local.Directory;

using System.Collections.Immutable;
using DigitalBrain;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Identity.Model;
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;

/// <summary>Durable local directory for accounts, principals, brains, memberships, and grants.</summary>
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
}

/// <summary>Emitted when directory state changes for a scope. Kept for historical journal compatibility.</summary>
[Signal("identity.directory-changed")]
public sealed record DirectoryChanged(string Scope) : Signal;

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

/// <summary>Persisted state of <see cref="DirectoryNeuron"/>.</summary>
public sealed record DirectoryState
{
    /// <summary>All created accounts.</summary>
    public Account[] Accounts { get; init; } = [];

    /// <summary>Registered principals.</summary>
    public PrincipalRecord[] Principals { get; init; } = [];

    /// <summary>Provisioned brains.</summary>
    public BrainRow[] Brains { get; init; } = [];

    /// <summary>Brain memberships.</summary>
    public Member[] Members { get; init; } = [];

    /// <summary>Capability grants.</summary>
    public Grant[] Grants { get; init; } = [];

    /// <summary>Completed brain creation operations.</summary>
    public BrainCreation[] Operations { get; init; } = [];

    internal Account? AccountOf(string accountId)
        => Accounts.FirstOrDefault(a => a.AccountId == accountId);

    internal PrincipalRecord? PrincipalOf(string principalId)
        => Principals.FirstOrDefault(p => p.PrincipalId == principalId);

    internal BrainRow? BrainOf(string brainId)
        => Brains.FirstOrDefault(b => b.BrainId == brainId);

    internal Member? OwnerMemberOf(string brainId)
        => Members.FirstOrDefault(m => m.BrainId == brainId && m.Role == MemberRole.Owner);

    internal Member? MemberOf(string brainScopeId, string principalId)
        => Members.FirstOrDefault(m => m.BrainId == brainScopeId && m.PrincipalId == principalId);

    internal Account? OwnerAccountOf(string brainId)
    {
        var accountId = BrainOf(brainId)?.AccountId ?? OwnerMemberOf(brainId)?.AccountId;
        return accountId is null ? null : AccountOf(accountId);
    }

    internal BrainDirectory Brain(string brainId)
    {
        var brain = BrainOf(brainId);
        var accountId = brain?.AccountId ?? OwnerMemberOf(brainId)?.AccountId ?? "";
        var kind = brain?.Kind ?? BrainKind.Personal;
        var members = Members.Where(m => m.BrainId == brainId).ToImmutableArray();
        var grants = Grants.Where(g => g.WorkspaceId == brainId).ToImmutableArray();
        return new BrainDirectory
        {
            BrainId = brainId,
            AccountId = accountId,
            Kind = kind,
            Members = members,
            Grants = grants,
        };
    }

    internal DirectoryState With(BrainDirectory brain)
    {
        var otherMembers = Members.Where(m => m.BrainId != brain.BrainId);
        var otherGrants = Grants.Where(g => g.WorkspaceId != brain.BrainId);
        var brainExists = Brains.Any(b => b.BrainId == brain.BrainId);
        var brains = brainExists
            ? Brains
            : [.. Brains, new BrainRow { BrainId = brain.BrainId, AccountId = brain.AccountId, Kind = brain.Kind }];
        return this with
        {
            Brains = brains,
            Members = [.. otherMembers, .. brain.Members],
            Grants = [.. otherGrants, .. brain.Grants],
        };
    }

    internal AccountBook ToAccountBook() => new()
    {
        Accounts = [.. Accounts],
        Principals = [.. Principals],
        Operations = [.. Operations],
    };

    internal DirectoryState With(AccountBook book) => this with
    {
        Accounts = [.. book.Accounts],
        Principals = [.. book.Principals],
        Operations = [.. book.Operations],
    };

    internal BrainEntry[] BrainsOf(string principalId)
        => Members
            .Where(m => m.PrincipalId == principalId)
            .Select(m => m.BrainId)
            .Distinct(StringComparer.Ordinal)
            .Select(brainId => new BrainEntry(brainId, BrainOf(brainId)?.Kind ?? BrainKind.Personal))
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
}

/// <summary>Neuron implementing <see cref="IDirectory"/>, delegating domain logic to pure aggregates.</summary>
[Neuron(IDirectory.Type)]
public sealed class DirectoryNeuron(TimeProvider? clock = null) : Neuron<DirectoryState>, IDirectory
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<Account?> ReadAccount(string accountId)
        => Task.FromResult(State.AccountOf(accountId));

    /// <inheritdoc />
    public Task<Account?> OwnerOfBrain(string brainId)
        => Task.FromResult(State.OwnerAccountOf(brainId));

    /// <inheritdoc />
    public async Task EnsureAccount(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var book = State.ToAccountBook();
        var (nextBook, _, facts) = book.EnsureAccount(account.AccountId, account.Name, account.OwnerPrincipalId, account.CreatedAt, _clock.GetUtcNow());
        if (facts.Length > 0)
        {
            await Save(State.With(nextBook), facts).ConfigureAwait(true);
        }
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

        var book = State.ToAccountBook();
        var (nextBook, provision, facts) = book.CreateBrain(accountId, principalId, operationId, displayName, kind, _clock.GetUtcNow());
        if (!provision.NewBrain)
        {
            return provision;
        }

        var next = State.With(nextBook);
        var brain = new BrainRow { BrainId = provision.Member.BrainId, AccountId = accountId, Kind = kind };
        next = next with
        {
            Brains = [.. next.Brains, brain],
            Members = [.. next.Members, provision.Member],
        };
        await Save(next, facts).ConfigureAwait(true);
        return provision;
    }

    /// <inheritdoc />
    public async Task<BrainProvision> EnsureBrain(Member owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (State.OwnerMemberOf(owner.BrainId) is { } existing)
        {
            return new BrainProvision(existing, false);
        }

        var now = _clock.GetUtcNow();
        var book = State.ToAccountBook();
        var (nextBook, _, accountFacts) = book.EnsureAccount(owner.AccountId, owner.DisplayName, owner.PrincipalId, owner.JoinedAt, now);

        var stamped = owner with
        {
            Role = MemberRole.Owner,
            JoinedAt = owner.JoinedAt == default ? now : owner.JoinedAt,
        };
        var next = State.With(nextBook);
        var brain = State.BrainOf(owner.BrainId) is null
            ? [.. next.Brains, new BrainRow { BrainId = owner.BrainId, AccountId = owner.AccountId, Kind = BrainKind.Personal }]
            : next.Brains;
        next = next with
        {
            Brains = brain,
            Members = [.. next.Members, stamped],
        };
        var facts = ImmutableArray.Create<Signal>(
            [.. accountFacts,
            new BrainCreated(owner.BrainId, owner.AccountId, BrainKind.Personal),
            new MemberAdded(stamped)]);
        await Save(next, facts).ConfigureAwait(true);
        return new BrainProvision(stamped, true);
    }

    /// <inheritdoc />
    public Task<BrainProvision> ProvisionOwner(Member owner)
        => EnsureBrain(owner);

    /// <inheritdoc />
    public Task<bool> IsRegistered(string principalId)
        => Task.FromResult(State.ToAccountBook().IsRegistered(principalId));

    /// <inheritdoc />
    public async Task<BrainProvision> RegisterPrincipal(string principalId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        var book = State.ToAccountBook();
        var (nextBook, provision, facts) = book.RegisterPrincipal(principalId, displayName, _clock.GetUtcNow());
        if (!provision.NewBrain)
        {
            return provision;
        }

        var next = State.With(nextBook);
        var brain = new BrainRow { BrainId = provision.Member.BrainId, AccountId = provision.Member.AccountId, Kind = BrainKind.Personal };
        next = next with
        {
            Brains = [.. next.Brains, brain],
            Members = [.. next.Members, provision.Member],
        };
        await Save(next, facts).ConfigureAwait(true);
        return provision;
    }

    /// <inheritdoc />
    public Task<BrainProvision?> CompleteRegistration(string principalId)
        => Task.FromResult(State.ToAccountBook().CompleteRegistration(principalId));

    /// <inheritdoc />
    public Task<BrainEntry[]> BrainsOf(string principalId)
        => Task.FromResult(State.BrainsOf(principalId));

    /// <inheritdoc />
    public Task<BrainKind> KindOf(string brainScopeId)
        => Task.FromResult(State.BrainOf(brainScopeId)?.Kind ?? BrainKind.Personal);

    /// <inheritdoc />
    public Task<bool> BrainExists(string brainScopeId)
        => Task.FromResult(State.BrainOf(brainScopeId) is not null);

    /// <inheritdoc />
    public Task<Member?> Membership(string brainScopeId, string principalId)
        => Task.FromResult(State.MemberOf(brainScopeId, principalId));

    /// <inheritdoc />
    public async Task<Member?> EnsureMember(CapabilityCaller caller, Member member)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(member);

        var brain = State.Brain(member.BrainId);
        var outcome = brain.AddMember(caller, member, _clock.GetUtcNow());
        var nextBrain = outcome.OrThrow(caller, member.BrainId);

        if (outcome.Changed)
        {
            await Save(State.With(nextBrain), outcome.Facts).ConfigureAwait(true);
        }

        return nextBrain.MemberOf(member.PrincipalId);
    }

    /// <inheritdoc />
    public async Task RemoveMember(CapabilityCaller caller, string brainScopeId, string accountId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var brain = State.Brain(brainScopeId);
        var outcome = brain.RemoveMember(caller, accountId);
        var nextBrain = outcome.OrThrow(caller, brainScopeId);

        if (outcome.Changed)
        {
            await Save(State.With(nextBrain), outcome.Facts).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task RemovePrincipalMember(CapabilityCaller caller, string brainScopeId, string principalId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var brain = State.Brain(brainScopeId);
        var outcome = brain.RemovePrincipal(caller, principalId);
        var nextBrain = outcome.OrThrow(caller, brainScopeId);

        if (outcome.Changed)
        {
            await Save(State.With(nextBrain), outcome.Facts).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task<CallDecision?> Authorize(CapabilityCaller caller, CallRequest request)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);

        var brain = State.Brain(caller.BrainId);
        var (outcome, decision) = brain.Authorize(caller, request);

        if (outcome.Changed)
        {
            await Save(State.With(outcome.Next), outcome.Facts).ConfigureAwait(true);
        }

        return decision;
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
        var brain = State.Brain(brainId);
        var outcome = brain.RecordGrant(caller, grant, _clock.GetUtcNow());
        if (outcome.Refused == Refusal.NotOwner)
        {
            return null;
        }

        var nextBrain = outcome.OrThrow(caller, brainId);
        if (outcome.Changed)
        {
            await Save(State.With(nextBrain), outcome.Facts).ConfigureAwait(true);
        }

        return nextBrain.Grants.FirstOrDefault(g => g.AppId == grant.AppId && g.SemanticTypeId == grant.SemanticTypeId && g.Mode == grant.Mode && g.WorkspaceId == brainId && !g.Revoked);
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsOwner(CapabilityCaller caller, string appId, string semanticTypeId, GrantMode mode)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var brain = State.Brain(caller.BrainId);
        var outcome = brain.RevokeGrant(caller, appId, semanticTypeId, mode);
        if (outcome.Refused == Refusal.NotOwner)
        {
            return false;
        }

        var nextBrain = outcome.OrThrow(caller, caller.BrainId);
        if (outcome.Changed)
        {
            await Save(State.With(nextBrain), outcome.Facts).ConfigureAwait(true);
        }

        return true;
    }
}
