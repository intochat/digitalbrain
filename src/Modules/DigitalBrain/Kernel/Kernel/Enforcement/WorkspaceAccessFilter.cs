using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core.Enforcement;

// The HTTP edge of the workspace membership rule. Routes that address a workspace by path apply
// this filter so a principal cannot reach a workspace it does not belong to, even though the route
// carries the workspace id. The open, single-owner development posture is not authenticated and
// keeps working; a cookie session or Basic credential is enforced.
public static class WorkspaceAccessFilter
{
    public static async ValueTask<object?> EnforceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        var http = context.HttpContext;
        var workspace = http.Request.RouteValues["workspace"] as string
            ?? http.Request.RouteValues["workspaceId"] as string;
        if (string.IsNullOrWhiteSpace(workspace) || !RequiresMembership(http))
        {
            return await next(context);
        }

        if (!CallerContextStamper.TryGet(out var caller) || caller is null) { return Results.Unauthorized(); }

        var access = http.RequestServices.GetRequiredService<IWorkspaceAccess>();
        return await access.CanAccessAsync(caller.PrincipalId, workspace, http.RequestAborted)
            ? await next(context)
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static bool RequiresMembership(HttpContext http)
        => http.User.Identity?.IsAuthenticated == true
            || http.Request.Headers.ContainsKey("Authorization");
}
