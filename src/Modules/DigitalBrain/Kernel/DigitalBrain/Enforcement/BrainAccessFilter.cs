using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core.Enforcement;

// The HTTP edge of the brain membership rule. Routes that address a brain by path apply
// this filter so a principal cannot reach a brain it does not belong to, even though the route
// carries the brain id. The open, single-owner development posture is not authenticated and
// keeps working; a cookie session or Basic credential is enforced.
public static class BrainAccessFilter
{
    public static async ValueTask<object?> EnforceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        var http = context.HttpContext;
        var brainId = http.Request.RouteValues["brainId"] as string;
        if (string.IsNullOrWhiteSpace(brainId)) { return await next(context); }

        return await Decide(http, brainId) is { } denied ? denied : await next(context);
    }

    // For a route that carries the brain in its body rather than its path.
    public static async ValueTask<IResult?> Decide(HttpContext http, string brainId)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!RequiresMembership(http)) { return null; }
        if (!CallerContextStamper.TryGet(out var caller)) { return Results.Unauthorized(); }

        var access = http.RequestServices.GetRequiredService<IBrainAccess>();
        return await access.CanAccessAsync(caller.PrincipalId, brainId, http.RequestAborted)
            ? null
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static bool RequiresMembership(HttpContext http)
        => http.User.Identity?.IsAuthenticated == true
            || http.Request.Headers.ContainsKey("Authorization");
}
