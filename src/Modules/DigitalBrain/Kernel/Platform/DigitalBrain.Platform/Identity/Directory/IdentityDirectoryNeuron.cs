using System.Security.Cryptography;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Platform.Identity.Directory;

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
        if (principalId is "owner" or "integrations" || Snapshot.Members.Any(m => m.PrincipalId == principalId))
        {
            throw new InvalidOperationException("That username is unavailable.");
        }
        var accountId = Guid.NewGuid().ToString("N");
        var member = NewMember(accountId, "account-" + accountId, principalId, displayName, MemberRole.Owner);
        Snapshot.Accounts.Add(new Account { AccountId = accountId, Name = displayName, OwnerPrincipalId = principalId, CreatedAt = DateTimeOffset.UtcNow });
        Snapshot.Members.Add(member);
        Snapshot.PasswordHashes.Add(principalId, Passwords.HashPassword(principalId, password));
        await _store.WriteStateAsync();
        await EstablishBrainAsync(member);
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
        // A principal may hold several memberships; sign in to the account it owns when there is one.
        return Snapshot.Members.Where(m => m.PrincipalId == principalId).OrderBy(m => m.Role).FirstOrDefault();
    }

    private readonly IPersistentState<IdentityDirectoryState> _store;

    public IdentityDirectoryNeuron(
        [PersistentState("directory", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<IdentityDirectoryState> store)
        : base(store)
        => _store = store;
    public Task<Member?> FindMemberAsync(string principalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Snapshot.Members.FirstOrDefault(member => string.Equals(member.PrincipalId, principalId, StringComparison.Ordinal)));
    }

    public Task<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(principalId) || string.IsNullOrWhiteSpace(brainId))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(Snapshot.Members.Any(member =>
            string.Equals(member.PrincipalId, principalId, StringComparison.Ordinal)
            && string.Equals(member.BrainId, brainId, StringComparison.Ordinal)));
    }

    public async Task<Member> ShareBrainAsync(string accountId, string brainId, string principalId, string displayName, MemberRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var next = Snapshot;
        var existing = next.Members.FirstOrDefault(member =>
            string.Equals(member.AccountId, accountId, StringComparison.Ordinal)
            && string.Equals(member.PrincipalId, principalId, StringComparison.Ordinal)
            && string.Equals(member.BrainId, brainId, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var member = NewMember(accountId, brainId, principalId, displayName, role);
        next.Members.Add(member);
        await _store.WriteStateAsync();
        return member;
    }

    private Task EstablishBrainAsync(Member member) =>
        GrainFactory.GetGrain<IBrain>(BrainScope.Create(member.AccountId, member.BrainId).Id)
            .Establish(new(member.BrainId, member.AccountId));

    private static Member NewMember(string accountId, string brainId, string principalId, string displayName, MemberRole role) => new()
    {
        AccountId = accountId,
        BrainId = brainId,
        PrincipalId = principalId,
        DisplayName = displayName,
        Role = role,
        JoinedAt = DateTimeOffset.UtcNow,
    };
}
