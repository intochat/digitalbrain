using System.Security.Claims;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal static class HttpActor
{
    public static ActorContext Require(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (http.User.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("An authenticated principal is required.");
        }

        var principalText = http.User.FindFirstValue(AuthOptions.PrincipalIdClaimType);
        if (string.IsNullOrWhiteSpace(principalText)
            || !Guid.TryParse(principalText, out var principalGuid)
            || principalGuid == Guid.Empty)
        {
            throw new InvalidOperationException("The authenticated principal is missing a PrincipalId claim.");
        }

        var username = http.User.Identity.Name
            ?? http.User.FindFirstValue(ClaimTypes.Name)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("The authenticated principal is missing a username.");
        }

        return new ActorContext(new PrincipalId(principalGuid), username);
    }

    public static bool TryGet(HttpContext http, out ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(http);
        actor = null!;

        if (http.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        try
        {
            actor = Require(http);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
