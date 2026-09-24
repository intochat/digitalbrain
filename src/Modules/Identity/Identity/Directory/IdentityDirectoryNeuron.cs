using System.Security.Cryptography;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Identity.Directory;

[GenerateSerializer, Alias("identity.directory-state")]
internal sealed record IdentityDirectoryState
{
    [Id(0)] public List<Account> Accounts { get; init; } = [];
    [Id(1)] public List<Member> Members { get; init; } = [];
    [Id(2)] public List<Invitation> Invitations { get; init; } = [];
    [Id(3)] public Dictionary<string, string> PasswordHashes { get; init; } = [];
}

[GrainType("identity-directory")]
internal sealed class IdentityDirectoryNeuron : Neuron<IdentityDirectoryState>, IIdentityDirectory
{
    private static readonly Microsoft.AspNetCore.Identity.PasswordHasher<string> Passwords = new();

    public async Task<Member> RegisterAsync(string principalId, string password, string displayName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(principalId) || principalId.Length > 80 ||
            !System.Text.RegularExpressions.Regex.IsMatch(principalId, "^[a-z0-9][a-z0-9-]*$"))
        {
            throw new ArgumentException("Use a lowercase username containing letters, numbers and hyphens.");
        }
        if (password is null || password.Length < 12 || password.Length > 256)
        {
            throw new ArgumentException("Use a password between 12 and 256 characters.");
        }
        if (principalId == "owner" || Snapshot.Members.Any(m => m.PrincipalId == principalId))
        {
            throw new InvalidOperationException("That username is unavailable.");
        }
        var accountId = Guid.NewGuid().ToString("N");
        var member = NewMember(accountId, "account-" + accountId, principalId, displayName, MemberRole.Owner);
        Snapshot.Accounts.Add(new Account { AccountId = accountId, Name = displayName, OwnerPrincipalId = principalId, CreatedAt = DateTimeOffset.UtcNow });
        Snapshot.Members.Add(member);
        Snapshot.PasswordHashes.Add(principalId, Passwords.HashPassword(principalId, password));
        await _store.WriteStateAsync();
        return member;
    }

    public async Task<Member?> AuthenticateAsync(string principalId, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(principalId) || string.IsNullOrEmpty(password) || password.Length > 256 ||
            !Snapshot.PasswordHashes.TryGetValue(principalId, out var hash))
        {
            return null;
        }
        var result = Passwords.VerifyHashedPassword(principalId, hash, password);
        if (result == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
        {
            return null;
        }
        if (result == Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded)
        {
            Snapshot.PasswordHashes[principalId] = Passwords.HashPassword(principalId, password);
            await _store.WriteStateAsync();
        }
        return Snapshot.Members.First(m => m.PrincipalId == principalId && m.Role == MemberRole.Owner);
    }

    private readonly IPersistentState<IdentityDirectoryState> _store;

    public IdentityDirectoryNeuron(
        [PersistentState("directory", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<IdentityDirectoryState> store)
        : base(store)
        => _store = store;
    public async Task<Account> CreateAccountAsync(string principalId, string name, string workspaceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var next = Snapshot;
        var account = new Account
        {
            AccountId = Guid.NewGuid().ToString("N"),
            Name = name,
            OwnerPrincipalId = principalId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        next.Accounts.Add(account);
        next.Members.Add(NewMember(account.AccountId, workspaceId, principalId, name, MemberRole.Owner));
        await _store.WriteStateAsync();
        return account;
    }

    public async Task<Member> EnsureOwnerAsync(string principalId, string workspaceId, string displayName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var existing = Snapshot.Members.FirstOrDefault(member => string.Equals(member.PrincipalId, principalId, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var next = Snapshot;
        var account = new Account
        {
            AccountId = Guid.NewGuid().ToString("N"),
            Name = displayName,
            OwnerPrincipalId = principalId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        next.Accounts.Add(account);

        var owner = NewMember(account.AccountId, workspaceId, principalId, displayName, MemberRole.Owner);
        next.Members.Add(owner);
        await _store.WriteStateAsync();
        return owner;
    }

    public async Task<Invitation> InviteAsync(string workspaceId, string? email, MemberRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var next = Snapshot;
        var invitation = new Invitation
        {
            Code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            AccountId = next.Members.FirstOrDefault(m => m.WorkspaceId == workspaceId && m.Role == MemberRole.Owner)?.AccountId ?? throw new InvalidOperationException("Create an account before inviting members."),
            WorkspaceId = workspaceId,
            Role = role,
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        next.Invitations.Add(invitation);
        await _store.WriteStateAsync();
        return invitation;
    }

    public async Task<Member> AcceptInvitationAsync(string code, string principalId, string displayName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        var next = Snapshot;
        var invitation = next.Invitations.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.Ordinal) && !item.Accepted)
            ?? throw new KeyNotFoundException("The invitation is not valid or was already accepted.");
        var replaced = next.Invitations.FindIndex(item => item.Code == invitation.Code);
        next.Invitations[replaced] = invitation with { Accepted = true };
        var member = NewMember(invitation.AccountId, invitation.WorkspaceId, principalId, displayName, invitation.Role);
        next.Members.Add(member);
        await _store.WriteStateAsync();
        return member;
    }

    public Task<Member?> FindMemberAsync(string principalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Snapshot.Members.FirstOrDefault(member => string.Equals(member.PrincipalId, principalId, StringComparison.Ordinal)));
    }

    public Task<bool> CanAccessAsync(string principalId, string workspaceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(principalId) || string.IsNullOrWhiteSpace(workspaceId))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(Snapshot.Members.Any(member =>
            string.Equals(member.PrincipalId, principalId, StringComparison.Ordinal)
            && string.Equals(member.WorkspaceId, workspaceId, StringComparison.Ordinal)));
    }

    public Task<IReadOnlyList<Member>> ListMembersAsync(string accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Member>>(
            [.. Snapshot.Members.Where(member => string.Equals(member.AccountId, accountId, StringComparison.Ordinal))]);
    }

    public async Task<Member> ShareWorkspaceAsync(string accountId, string workspaceId, string principalId, string displayName, MemberRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var next = Snapshot;
        var existing = next.Members.FirstOrDefault(member =>
            string.Equals(member.AccountId, accountId, StringComparison.Ordinal)
            && string.Equals(member.PrincipalId, principalId, StringComparison.Ordinal)
            && string.Equals(member.WorkspaceId, workspaceId, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var member = NewMember(accountId, workspaceId, principalId, displayName, role);
        next.Members.Add(member);
        await _store.WriteStateAsync();
        return member;
    }

    private static Member NewMember(string accountId, string workspaceId, string principalId, string displayName, MemberRole role) => new()
    {
        AccountId = accountId,
        WorkspaceId = workspaceId,
        PrincipalId = principalId,
        DisplayName = displayName,
        Role = role,
        JoinedAt = DateTimeOffset.UtcNow,
    };
}
