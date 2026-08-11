using System.Security.Claims;

namespace DigitalBrain.Kernel;

internal sealed class LoopbackDevAuthMiddleware(
    RequestDelegate next,
    LoopbackDevAuthOptions options,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context, IAccountDirectory accounts)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(accounts);

        if (options.Enabled
            && environment.IsDevelopment()
            && context.User.Identity?.IsAuthenticated != true
            && RequestNetwork.IsLoopback(context))
        {
            var owner = await accounts.FindBootstrapOwnerAsync(context.RequestAborted).ConfigureAwait(false);
            if (owner is not null)
            {
                var identity = new ClaimsIdentity(AuthHostingExtensions.AuthenticationScheme);
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, owner.Id));
                identity.AddClaim(new Claim(ClaimTypes.Name, owner.UserName));
                identity.AddClaim(new Claim(AuthOptions.PrincipalIdClaimType, owner.PrincipalId.ToString("N")));
                identity.AddClaim(new Claim(AuthOptions.BootstrapOwnerClaimType, "1"));
                context.User = new ClaimsPrincipal(identity);
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
