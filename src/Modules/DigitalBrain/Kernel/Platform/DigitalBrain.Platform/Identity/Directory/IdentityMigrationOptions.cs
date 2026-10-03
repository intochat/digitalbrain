using DigitalBrain.Platform.Contracts.Identity;
using Orleans;
using DigitalBrain.Platform.Identity.Authority;
using DigitalBrain.Platform.Identity.Configuration;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace DigitalBrain.Platform.Identity.Directory;

public sealed class IdentityMigrationOptions
{
    public const string SectionName = "DigitalBrain:Identity:Migration";
    public bool Maintenance { get; set; }
    // Explicit mapping for legacy grants without a corresponding directory membership.
    public Dictionary<string, string> LegacyBrainAccounts { get; set; } = [];
}

internal sealed class IdentityMigrationStartup(IGrainFactory grains, IOptions<AuthOptions> auth, TimeProvider clock) : IStartupTask
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        await grains.GetGrain<IIdentityDirectory>(IdentityGrains.Directory).PrepareAsync(cancellationToken);
        // Basic bootstrap is an explicit owner of one default scope, never a wildcard bypass.
        if (auth.Value.Username is not { Length: > 0 } principal || string.IsNullOrEmpty(auth.Value.Password)) { return; }
        var account = grains.GetGrain<IAccount>(principal);
        var now = clock.GetUtcNow();
        await account.EnsureCreated(new Account { AccountId = principal, OwnerPrincipalId = principal, Name = principal, CreatedAt = now });
        await account.EnsureBrain(new Member { PrincipalId = principal, AccountId = principal, BrainId = AccountSession.DefaultLogin,
            DisplayName = principal, Role = MemberRole.Owner, JoinedAt = now });
    }
}
