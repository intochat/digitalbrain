using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Authority;

namespace DigitalBrain.Testing.Module;

public static class BrainIdentityExtensions
{
    public static Task AuthorizeCallerAsync(this ModuleBrain brain)
    {
        var caller = CallerContextStamper.Require();
        return brain.Grains.GetGrain<IBrainAuthority>(BrainScope.CurrentId()).ProvisionOwner(new Member
        {
            PrincipalId = caller.PrincipalId,
            AccountId = caller.AccountId,
            BrainId = caller.BrainId,
            Role = MemberRole.Owner,
            DisplayName = caller.PrincipalId,
        });
    }
}
