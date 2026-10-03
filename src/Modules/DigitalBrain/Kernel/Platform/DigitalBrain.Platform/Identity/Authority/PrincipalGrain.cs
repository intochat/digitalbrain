using DigitalBrain.Contracts;
using DigitalBrain.Platform.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Platform.Identity.Authority;

[GenerateSerializer, Alias("identity.principal-state.v2")]
internal sealed record PrincipalState
{
    [Id(0)] public Member? DefaultMembership { get; init; }
    [Id(1)] public string? PasswordHash { get; init; }
    [Id(2)] public bool RegistrationComplete { get; init; }
}

[GrainType("identity.principal.v2")]
internal sealed class PrincipalGrain(
    [PersistentState("principal", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<PrincipalState> storage,
    TimeProvider clock) : IdentityStateGrain<PrincipalState>(storage), IPrincipal
{
    private static readonly PasswordHasher<string> Passwords = new();

    public async Task<Member> Register(string password, string displayName)
    {
        var principal = this.GetPrimaryKeyString();
        if (principal.Length is 0 or > 80 || !System.Text.RegularExpressions.Regex.IsMatch(principal, "^[a-z0-9][a-z0-9-]*$"))
        { throw new ArgumentException("Use a lowercase username containing letters, numbers and hyphens."); }
        if (password is null || password.Length is < 12 or > 256)
        { throw new ArgumentException("Use a password between 12 and 256 characters."); }
        if (principal is "owner" or "integrations") { throw new InvalidOperationException("That username is unavailable."); }
        if (State.DefaultMembership is not null)
        {
            if (State.RegistrationComplete || Passwords.VerifyHashedPassword(principal, State.PasswordHash!, password) == PasswordVerificationResult.Failed)
            { throw new InvalidOperationException("That username is unavailable."); }
            return await CompleteRegistration();
        }
        var accountId = Guid.NewGuid().ToString("N");
        var member = new Member { PrincipalId = principal, AccountId = accountId, BrainId = "account-" + accountId,
            DisplayName = displayName, Role = MemberRole.Owner, JoinedAt = clock.GetUtcNow() };
        await Persist(new() { DefaultMembership = member, PasswordHash = Passwords.HashPassword(principal, password) });
        return await CompleteRegistration();
    }

    private async Task<Member> CompleteRegistration()
    {
        var member = State.DefaultMembership!;
        var account = GrainFactory.GetGrain<IAccount>(member.AccountId);
        await account.EnsureCreated(new Account { AccountId = member.AccountId, OwnerPrincipalId = member.PrincipalId,
            Name = member.DisplayName, CreatedAt = member.JoinedAt });
        await account.EnsureBrain(member);
        await Persist(State with { RegistrationComplete = true });
        return member;
    }

    public async Task<Member?> Authenticate(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 256 || State.PasswordHash is null) { return null; }
        var result = Passwords.VerifyHashedPassword(this.GetPrimaryKeyString(), State.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) { return null; }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        { await Persist(State with { PasswordHash = Passwords.HashPassword(this.GetPrimaryKeyString(), password) }); }
        if (!State.RegistrationComplete) { return await CompleteRegistration(); }
        return State.DefaultMembership;
    }

    public Task<Member?> DefaultMembership() => Task.FromResult(State.DefaultMembership);
    public async Task Import(Member member, string passwordHash)
    {
        if (member.PrincipalId != this.GetPrimaryKeyString()) { throw new InvalidOperationException("Principal import key mismatch."); }
        if (State.DefaultMembership is not null)
        {
            if (State.DefaultMembership != member || State.PasswordHash != passwordHash)
            { throw new InvalidOperationException("Principal import conflicts with existing state."); }
            return;
        }
        await Persist(new() { DefaultMembership = member, PasswordHash = passwordHash, RegistrationComplete = true });
    }
}
