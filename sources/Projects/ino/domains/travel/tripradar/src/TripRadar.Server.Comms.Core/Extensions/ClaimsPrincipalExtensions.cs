using System.Security.Claims;

namespace TripRadar.Server.Comms.Core.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal? principal, out long userId)
    {
        userId = 0;
        return principal?.Identity?.IsAuthenticated == true &&
               long.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value, out userId) && userId > 0;
    }

    public static string? GetUsername(this ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated != true
            ? null
            : principal.FindFirst(ClaimTypes.Name)?.Value ??
              principal.FindFirst("name")?.Value ??
              principal.FindFirst("unique_name")?.Value;
}
