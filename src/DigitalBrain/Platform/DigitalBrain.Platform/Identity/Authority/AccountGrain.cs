using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Platform.Identity.Authority;

[GenerateSerializer, Alias("identity.account-state.v2")]
internal sealed record AccountState
{
    [Id(0)] public Account? Account { get; init; }
    [Id(1)] public Dictionary<string, Member> BrainOperations { get; init; } = [];
}

[GrainType("identity.account.v2")]
internal sealed class AccountGrain(
    [PersistentState("account", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AccountState> storage,
    TimeProvider clock) : IdentityStateGrain<AccountState>(storage), IAccount
{
    public async Task EnsureCreated(Account account)
    {
        if (account.AccountId != this.GetPrimaryKeyString()) { throw new InvalidOperationException("Account key mismatch."); }
        if (State.Account is not null)
        {
            if (State.Account.OwnerPrincipalId != account.OwnerPrincipalId) { throw new InvalidOperationException("Account ownership conflict."); }
            return;
        }
        await Persist(State with { Account = account });
    }
    public Task<Account?> Read() => Task.FromResult(State.Account);
    public async Task<Member> CreateBrain(string principalId, string operationId, string displayName)
    {
        if (State.Account?.OwnerPrincipalId != principalId) { throw new UnauthorizedAccessException("Only the account owner can create a brain."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (!State.BrainOperations.TryGetValue(operationId, out var member))
        {
            member = new Member
            {
                PrincipalId = principalId,
                AccountId = this.GetPrimaryKeyString(),
                BrainId = "workspace-" + Guid.NewGuid().ToString("N"),
                DisplayName = displayName,
                Role = MemberRole.Owner,
                JoinedAt = clock.GetUtcNow()
            };
            await Persist(State with { BrainOperations = new(State.BrainOperations) { [operationId] = member } });
        }
        await EnsureBrain(member);
        return member;
    }
    public async Task EnsureBrain(Member owner)
    {
        if (State.Account is null || owner.AccountId != this.GetPrimaryKeyString() || State.Account.OwnerPrincipalId != owner.PrincipalId)
        { throw new UnauthorizedAccessException("Brain ownership does not match the account."); }
        var scope = BrainScope.Create(owner.AccountId, owner.BrainId);
        await GrainFactory.GetGrain<IBrain>(scope.Id).Establish(new(owner.BrainId, owner.AccountId));
        var authority = GrainFactory.GetGrain<IBrainAuthority>(scope.Id);
        await authority.Initialize(owner.AccountId, owner.BrainId);
        await authority.ProvisionOwner(owner);
    }
}
