using DigitalBrain.Abstractions;
using Orleans.Runtime;

namespace DigitalBrain.Identity.State;

// A single non-reentrant grain makes account linking, code consumption and session issuance atomic.
// Identity state is infrastructure: it deliberately does not implement INeuron or expose snapshots.
[GrainType("identity-directory")]
internal sealed class IdentityDirectoryGrain(
    [PersistentState("identity", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<IdentityDirectoryState> state,
    TimeProvider clock) : Grain, IIdentityDirectory
{
    public async Task<IdentitySession?> SignIn(VerifiedExternalIdentity identity, TimeSpan lifetime)
    {
        ValidateLifetime(lifetime);
        if (!state.State.ExternalLinks.TryGetValue(ExternalKey(identity), out var userId)
            || state.State.Accounts[userId].Disabled)
        {
            return null;
        }
        var session = IssueSession(userId, lifetime);
        await Save().ConfigureAwait(true);
        return session;
    }

    public async Task<IdentitySession> BootstrapOwner(VerifiedExternalIdentity identity, string workspaceId, TimeSpan lifetime)
    {
        ValidateLifetime(lifetime);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var key = ExternalKey(identity);
        if (state.State.OwnerUserId is not { } userId)
        {
            if (state.State.Accounts.Count != 0 || state.State.ExternalLinks.Count != 0
                || state.State.Sessions.Count != 0 || state.State.LinkCodes.Count != 0 || state.State.Grants.Count != 0)
            {
                throw new InvalidOperationException("Identity state has no owner but is not empty; refusing to bootstrap over existing accounts.");
            }
            userId = Guid.NewGuid().ToString("N");
            state.State.Accounts.Add(userId, new AccountState());
            state.State.OwnerUserId = userId;
            state.State.ExternalLinks.Add(key, userId);
        }
        else if (!state.State.ExternalLinks.TryGetValue(key, out var linkedUser) || linkedUser != userId)
        {
            throw new UnauthorizedAccessException("Owner bootstrap identity does not match the existing owner.");
        }
        if (state.State.Accounts[userId].Disabled)
        {
            throw new UnauthorizedAccessException("Account disabled.");
        }
        state.State.Accounts[userId].Memberships[workspaceId] = WorkspaceRole.Owner;
        var session = IssueSession(userId, lifetime);
        await Save().ConfigureAwait(true);
        return session;
    }

    public Task<AuthenticatedIdentity?> Authenticate(string sessionHash) => Task.FromResult(AuthenticateCore(sessionHash));

    public async Task RevokeSession(string sessionHash)
    {
        state.State.Sessions.Remove(sessionHash);
        await Save().ConfigureAwait(true);
    }

    public async Task<string> CreateLinkCode(string sessionHash, TimeSpan lifetime)
    {
        ValidateLifetime(lifetime);
        if (lifetime > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }
        var actor = RequireSession(sessionHash);
        var code = IdentityService.NewSecret();
        state.State.LinkCodes.Add(IdentityService.Hash(code), new(actor.UserId, sessionHash, clock.GetUtcNow() + lifetime));
        await Save().ConfigureAwait(true);
        return code;
    }

    public async Task<IdentitySession?> RedeemLinkCode(string codeHash, VerifiedExternalIdentity identity, TimeSpan sessionLifetime)
    {
        ValidateLifetime(sessionLifetime);
        var key = ExternalKey(identity);
        if (!state.State.LinkCodes.TryGetValue(codeHash, out var code) || code.ExpiresAt <= clock.GetUtcNow()
            || AuthenticateCore(code.SessionHash) is not { } actor || actor.UserId != code.UserId)
        {
            return null;
        }
        if (state.State.ExternalLinks.TryGetValue(key, out var existingUser) && existingUser != code.UserId)
        {
            return null;
        }
        state.State.ExternalLinks[key] = code.UserId;
        state.State.LinkCodes.Remove(codeHash);
        var session = IssueSession(code.UserId, sessionLifetime);
        await Save().ConfigureAwait(true);
        return session;
    }

    public Task<IdentityAccount?> GetAccount(string userId)
        => Task.FromResult(state.State.Accounts.TryGetValue(userId, out var account)
            ? new IdentityAccount(userId, account.Disabled, new(account.Memberships), userId == state.State.OwnerUserId) : null);

    public async Task<IdentityAccount> CreateMember(string sessionHash, VerifiedExternalIdentity identity, string workspaceId)
    {
        RequireWorkspaceOwner(sessionHash, workspaceId);
        var key = ExternalKey(identity);
        if (state.State.ExternalLinks.ContainsKey(key))
        {
            throw new InvalidOperationException("External identity is already linked.");
        }
        var userId = Guid.NewGuid().ToString("N");
        var account = new AccountState();
        account.Memberships.Add(workspaceId, WorkspaceRole.Member);
        state.State.Accounts.Add(userId, account);
        state.State.ExternalLinks.Add(key, userId);
        await Save().ConfigureAwait(true);
        return new(userId, false, new(account.Memberships), false);
    }

    public async Task SetAccountDisabled(string sessionHash, string userId, bool disabled)
    {
        if (!RequireSession(sessionHash).IsOwner)
        {
            throw new UnauthorizedAccessException();
        }
        state.State.Accounts[userId].Disabled = disabled;
        if (disabled)
        {
            foreach (var key in state.State.Sessions.Where(entry => entry.Value.UserId == userId).Select(entry => entry.Key).ToArray())
            {
                state.State.Sessions.Remove(key);
            }
            foreach (var key in state.State.Grants.Where(entry => entry.Value.UserId == userId).Select(entry => entry.Key).ToArray())
            {
                state.State.Grants.Remove(key);
            }
        }
        await Save().ConfigureAwait(true);
    }

    public async Task SetMembership(string sessionHash, string workspaceId, string userId, WorkspaceRole role)
    {
        RequireWorkspaceOwner(sessionHash, workspaceId);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }
        state.State.Accounts[userId].Memberships[workspaceId] = role;
        await Save().ConfigureAwait(true);
    }

    public async Task<string> GrantAutomation(string sessionHash, string workspaceId, string automationId, string[] targets, string[] actions)
    {
        var actor = RequireWorkspaceOwner(sessionHash, workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        ValidateScope(targets);
        ValidateScope(actions);
        var grantId = Guid.NewGuid().ToString("N");
        state.State.Grants.Add(grantId, new(actor.UserId, workspaceId, automationId, targets.ToArray(), actions.ToArray()));
        await Save().ConfigureAwait(true);
        return grantId;
    }

    public async Task RevokeAutomation(string sessionHash, string grantId)
    {
        var actor = RequireSession(sessionHash);
        if (!state.State.Grants.TryGetValue(grantId, out var grant))
        {
            return;
        }
        if (!actor.Memberships.TryGetValue(grant.WorkspaceId, out var role) || role != WorkspaceRole.Owner)
        {
            throw new UnauthorizedAccessException();
        }
        state.State.Grants.Remove(grantId);
        await Save().ConfigureAwait(true);
    }

    public Task<bool> IsAutomationAuthorized(string grantId, string workspaceId, string automationId, string target, string action)
        => Task.FromResult(state.State.Grants.TryGetValue(grantId, out var grant)
            && grant.WorkspaceId == workspaceId && grant.AutomationId == automationId
            && grant.Targets.Contains(target, StringComparer.Ordinal) && grant.Actions.Contains(action, StringComparer.Ordinal)
            && state.State.Accounts.TryGetValue(grant.UserId, out var account) && !account.Disabled
            && account.Memberships.TryGetValue(workspaceId, out var role) && role == WorkspaceRole.Owner);

    public Task<bool> OwnsAutomationGrant(string userId, string grantId, string workspaceId, string automationId)
        => Task.FromResult(state.State.Grants.TryGetValue(grantId, out var grant)
            && grant.UserId == userId && grant.WorkspaceId == workspaceId && grant.AutomationId == automationId
            && state.State.Accounts.TryGetValue(userId, out var account) && !account.Disabled
            && account.Memberships.TryGetValue(workspaceId, out var role) && role == WorkspaceRole.Owner);

    private AuthenticatedIdentity? AuthenticateCore(string sessionHash)
    {
        if (!state.State.Sessions.TryGetValue(sessionHash, out var session) || session.ExpiresAt <= clock.GetUtcNow()
            || !state.State.Accounts.TryGetValue(session.UserId, out var account) || account.Disabled)
        {
            return null;
        }
        return new(session.UserId, session.UserId == state.State.OwnerUserId, session.CsrfToken, new(account.Memberships));
    }

    private AuthenticatedIdentity RequireSession(string sessionHash)
        => AuthenticateCore(sessionHash) ?? throw new UnauthorizedAccessException("A current authenticated session is required.");

    private AuthenticatedIdentity RequireWorkspaceOwner(string sessionHash, string workspaceId)
    {
        var actor = RequireSession(sessionHash);
        if (!actor.Memberships.TryGetValue(workspaceId, out var role) || role != WorkspaceRole.Owner)
        {
            throw new UnauthorizedAccessException("Workspace owner membership is required.");
        }
        return actor;
    }

    private IdentitySession IssueSession(string userId, TimeSpan lifetime)
    {
        var token = IdentityService.NewSecret();
        var csrf = IdentityService.NewSecret();
        var expires = clock.GetUtcNow() + lifetime;
        state.State.Sessions.Add(IdentityService.Hash(token), new(userId, csrf, expires));
        return new(token, csrf, userId, userId == state.State.OwnerUserId, expires);
    }

    private async Task Save()
    {
        var now = clock.GetUtcNow();
        foreach (var key in state.State.Sessions.Where(entry => entry.Value.ExpiresAt <= now).Select(entry => entry.Key).ToArray())
        {
            state.State.Sessions.Remove(key);
        }
        foreach (var key in state.State.LinkCodes.Where(entry => entry.Value.ExpiresAt <= now
            || !state.State.Sessions.ContainsKey(entry.Value.SessionHash)).Select(entry => entry.Key).ToArray())
        {
            state.State.LinkCodes.Remove(key);
        }
        try { await state.WriteStateAsync().ConfigureAwait(true); }
        catch
        {
            // Never authorize against a mutation whose durable write failed.
            DeactivateOnIdle();
            state.State = new();
            await state.ReadStateAsync().ConfigureAwait(true);
            throw;
        }
    }

    private static string ExternalKey(VerifiedExternalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Subject);
        return $"{identity.Provider.Length}:{identity.Provider}{identity.Subject}";
    }

    private static void ValidateLifetime(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(30))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }
    }

    private static void ValidateScope(string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace) || values.Contains("*", StringComparer.Ordinal))
        {
            throw new ArgumentException("At least one explicit scope is required; wildcards are not supported.", nameof(values));
        }
    }
}
