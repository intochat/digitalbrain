using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Auth;

public sealed class DigitalBrainClaimsPrincipalFactory(
    UserManager<DigitalBrainUser> users,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<DigitalBrainUser>(users, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(DigitalBrainUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);
        identity.AddClaim(new Claim(AuthOptions.PrincipalIdClaimType, user.PrincipalId.ToString("N")));
        if (user.IsBootstrapOwner)
        {
            identity.AddClaim(new Claim(AuthOptions.BootstrapOwnerClaimType, "1"));
        }

        return identity;
    }
}
