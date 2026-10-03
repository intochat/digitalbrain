using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;


namespace DigitalBrain.Kernel.AspNetCore;

// The HTTP edge of the brain membership rule. Routes that address a brain by path apply
// this filter so a principal cannot reach a brain it does not belong to, even though the route
// carries the brain id. Every request answers it: the composed IBrainAccess carries the host's
// posture, so openness is a composition decision, never inferred from the request's shape.
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
        if (!CallerContextStamper.TryGet(out var caller)) { return Results.Unauthorized(); }

        var access = http.RequestServices.GetRequiredService<IBrainAccess>();
        return await access.CanAccessAsync(caller.PrincipalId, caller.AccountId, brainId, http.RequestAborted)
            ? null
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }
}
