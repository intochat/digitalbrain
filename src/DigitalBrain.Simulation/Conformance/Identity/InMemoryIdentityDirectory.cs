namespace DigitalBrain.Testing.Conformance.Identity;

using System.Collections.Immutable;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Identity.Model;

/// <summary>
/// An in-memory <see cref="IIdentityDirectoryPort"/> and <see cref="IIdentitySessionPort"/> delegating directly
/// to pure domain models <see cref="BrainDirectory"/>, <see cref="AccountBook"/>, and <see cref="SessionBook"/>.
/// </summary>
public sealed class InMemoryIdentityDirectory : IIdentityDirectoryPort, IIdentitySessionPort
{
    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromHours(8);

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private AccountBook _accounts = new();
    private readonly Dictionary<string, BrainDirectory> _brains = new(StringComparer.Ordinal);
    private SessionBook _sessions = new();
    private readonly List<ISignInHandler> _handlers = [];

    /// <summary>Creates the directory.</summary>
    /// <param name="time">The clock for created, joined and granted times.</param>
    public InMemoryIdentityDirectory(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>What <see cref="Describe"/> answers; a Secured <c>memory</c> implementation without an account surface by default.</summary>
    public IdentityProviderDescriptor Descriptor { get; set; } = new() { ProviderId = "memory", Posture = IdentityPosture.Secured };

    /// <summary>Simulates a host sign-in: registers <paramref name="principalId"/> if needed and invokes handlers.</summary>
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
#pragma warning disable CA1031
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
            }
        }

        return provision.Member;
    }

    /// <summary>Adds a sign-in handler.</summary>
    public void AddSignInHandler(ISignInHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate) { _handlers.Add(handler); }
    }

    /// <inheritdoc />
    public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken)
    {
        lock (_gate) { return Task.FromResult(_accounts.AccountOf(accountId)); }
    }

    /// <inheritdoc />
    public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var brain = BrainLocked(brainId);
            var accountId = brain.AccountId;
            if (string.IsNullOrEmpty(accountId))
            {
                accountId = brain.Owner?.AccountId ?? "";
            }

            return Task.FromResult(string.IsNullOrEmpty(accountId) ? null : _accounts.AccountOf(accountId));
        }
    }

    /// <inheritdoc />
    public Task EnsureAccount(Account account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        lock (_gate)
        {
            var (next, _, _) = _accounts.EnsureAccount(account.AccountId, account.Name, account.OwnerPrincipalId, account.CreatedAt, _time.GetUtcNow());
            _accounts = next;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        lock (_gate)
        {
            var (next, provision, _) = _accounts.CreateBrain(accountId, principalId, operationId, displayName, kind, _time.GetUtcNow());
            _accounts = next;
            if (provision.NewBrain)
            {
                var brain = new BrainDirectory
                {
                    BrainId = provision.Member.BrainId,
                    AccountId = accountId,
                    Kind = kind,
                    Members = [provision.Member],
                };
                _brains[provision.Member.BrainId] = brain;
            }

            return Task.FromResult(provision);
        }
    }

    /// <inheritdoc />
    public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_gate)
        {
            var brain = BrainLocked(owner.BrainId);
            if (brain.Owner is { } existing)
            {
                return Task.FromResult(new BrainProvision(existing, false));
            }

            var now = _time.GetUtcNow();
            var (nextAccounts, _, _) = _accounts.EnsureAccount(owner.AccountId, owner.DisplayName, owner.PrincipalId, owner.JoinedAt, now);
            _accounts = nextAccounts;

            var stamped = owner with
            {
                Role = MemberRole.Owner,
                JoinedAt = owner.JoinedAt == default ? now : owner.JoinedAt,
            };
            var updatedBrain = brain with
            {
                AccountId = owner.AccountId,
                Kind = brain.Kind,
                Members = brain.Members.Add(stamped),
            };
            _brains[owner.BrainId] = updatedBrain;
            return Task.FromResult(new BrainProvision(stamped, true));
        }
    }

    /// <inheritdoc />
    public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken)
        => EnsureBrain(owner, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken)
    {
        lock (_gate) { return Task.FromResult(_accounts.IsRegistered(principalId)); }
    }

    /// <inheritdoc />
    public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        lock (_gate)
        {
            var (next, provision, _) = _accounts.RegisterPrincipal(principalId, displayName, _time.GetUtcNow());
            _accounts = next;
            if (provision.NewBrain)
            {
                var brain = new BrainDirectory
                {
                    BrainId = provision.Member.BrainId,
                    AccountId = provision.Member.AccountId,
                    Kind = BrainKind.Personal,
                    Members = [provision.Member],
                };
                _brains[provision.Member.BrainId] = brain;
            }

            return Task.FromResult(provision);
        }
    }

    /// <inheritdoc />
    public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken)
    {
        lock (_gate) { return Task.FromResult(_accounts.CompleteRegistration(principalId)); }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var list = _brains.Values
                .Where(b => b.Members.Any(m => m.PrincipalId == principalId))
                .Select(b => new BrainEntry(b.BrainId, b.Kind))
                .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult<IReadOnlyList<BrainEntry>>(list);
        }
    }

    /// <inheritdoc />
    public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken)
    {
        lock (_gate) { return Task.FromResult(_brains.TryGetValue(brainScopeId, out var brain) ? brain.Kind : BrainKind.Personal); }
    }

    /// <inheritdoc />
    public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken)
    {
        lock (_gate) { return Task.FromResult(_brains.ContainsKey(brainScopeId)); }
    }

    /// <inheritdoc />
    public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken)
    {
        lock (_gate) { return Task.FromResult(_brains.TryGetValue(brainScopeId, out var brain) ? brain.MemberOf(principalId) : null); }
    }

    /// <inheritdoc />
    public Task<Member?> EnsureMember(CapabilityCaller caller, Member member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(member);

        lock (_gate)
        {
            var brain = BrainLocked(member.BrainId);
            var outcome = brain.AddMember(caller, member, _time.GetUtcNow());
            var nextBrain = outcome.OrThrow(caller, member.BrainId);
            _brains[member.BrainId] = nextBrain;
            return Task.FromResult(nextBrain.MemberOf(member.PrincipalId));
        }
    }

    /// <inheritdoc />
    public Task RemoveMember(CapabilityCaller caller, string brainScopeId, string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        lock (_gate)
        {
            var brain = BrainLocked(brainScopeId);
            var outcome = brain.RemoveMember(caller, accountId);
            var nextBrain = outcome.OrThrow(caller, brainScopeId);
            _brains[brainScopeId] = nextBrain;
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task RemovePrincipalMember(CapabilityCaller caller, string brainScopeId, string principalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        lock (_gate)
        {
            var brain = BrainLocked(brainScopeId);
            var outcome = brain.RemovePrincipal(caller, principalId);
            var nextBrain = outcome.OrThrow(caller, brainScopeId);
            _brains[brainScopeId] = nextBrain;
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<CallDecision?> Authorize(CapabilityCaller caller, CallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var brain = BrainLocked(caller.BrainId);
            var (outcome, decision) = brain.Authorize(caller, request);
            _brains[caller.BrainId] = outcome.Next;
            return Task.FromResult<CallDecision?>(decision);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var brain = BrainLocked(brainScopeId);
            var active = brain.Grants.Where(g => !g.Revoked).ToArray();
            return Task.FromResult<IReadOnlyList<Grant>>(active);
        }
    }

    /// <inheritdoc />
    public Task<Grant?> GrantAsOwner(CapabilityCaller caller, Grant grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(grant);

        lock (_gate)
        {
            var brainId = string.IsNullOrWhiteSpace(grant.WorkspaceId) ? caller.BrainId : grant.WorkspaceId;
            var brain = BrainLocked(brainId);
            var outcome = brain.RecordGrant(caller, grant, _time.GetUtcNow());
            if (outcome.Refused == Refusal.NotOwner)
            {
                return Task.FromResult<Grant?>(null);
            }

            var nextBrain = outcome.OrThrow(caller, brainId);
            _brains[brainId] = nextBrain;
            var stored = nextBrain.Grants.FirstOrDefault(g => g.AppId == grant.AppId && g.SemanticTypeId == grant.SemanticTypeId && g.Mode == grant.Mode && g.WorkspaceId == brainId && !g.Revoked);
            return Task.FromResult(stored);
        }
    }

    /// <inheritdoc />
    public Task<bool> RevokeAsOwner(CapabilityCaller caller, string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        lock (_gate)
        {
            var brain = BrainLocked(caller.BrainId);
            var outcome = brain.RevokeGrant(caller, appId, semanticTypeId, mode);
            if (outcome.Refused == Refusal.NotOwner)
            {
                return Task.FromResult(false);
            }

            var nextBrain = outcome.OrThrow(caller, caller.BrainId);
            _brains[caller.BrainId] = nextBrain;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<SessionIssued> Issue(CapabilityCaller caller, SessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        SessionClaims.Validate(request);

        lock (_gate)
        {
            var member = request.Member;
            var current = BrainLocked(member.BrainId).MemberOf(member.PrincipalId);
            if (current is null || current.AccountId != member.AccountId)
            {
                throw new InvalidOperationException($"{member.PrincipalId} is not a member of {member.BrainId}; no session is issued.");
            }

            var sessionId = $"session-{Guid.NewGuid():N}";
            var tokenHash = SessionBook.HashToken(sessionId);
            var expiresAt = _time.GetUtcNow() + (request.Lifetime ?? DefaultSessionLifetime);
            var (nextSessions, _, _) = _sessions.Start(tokenHash, member.BrainId, member.PrincipalId, expiresAt, _time.GetUtcNow());
            _sessions = nextSessions;
            return Task.FromResult(new SessionIssued(sessionId, expiresAt));
        }
    }

    /// <inheritdoc />
    public Task End(CapabilityCaller caller, string sessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(sessionId);

        lock (_gate)
        {
            var tokenHash = SessionBook.HashToken(sessionId);
            var (nextSessions, _) = _sessions.End(tokenHash);
            _sessions = nextSessions;
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<Member?> Validate(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        lock (_gate)
        {
            var tokenHash = SessionBook.HashToken(sessionId);
            var session = _sessions.Find(tokenHash, _time.GetUtcNow());
            if (session is null)
            {
                return Task.FromResult<Member?>(null);
            }

            return Task.FromResult(BrainLocked(session.BrainId).MemberOf(session.PrincipalId));
        }
    }

    /// <inheritdoc />
    public IdentityProviderDescriptor Describe() => Descriptor;

    private BrainDirectory BrainLocked(string brainId)
    {
        if (!_brains.TryGetValue(brainId, out var brain))
        {
            brain = new BrainDirectory { BrainId = brainId };
            _brains[brainId] = brain;
        }

        return brain;
    }
}
