using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.ProductHost.Protocol;

public sealed record ProductCaller(string Workspace, string Principal)
{
    public static ProductCaller From(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return new ProductCaller("local", "owner");
        }

        var workspace = context.User.FindFirstValue("brain_workspace");
        var principal = context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(principal))
        {
            throw new BadHttpRequestException(
                "The authenticated caller has no DigitalBrain workspace or subject claim.",
                StatusCodes.Status401Unauthorized);
        }

        return new ProductCaller(workspace, principal);
    }
}
