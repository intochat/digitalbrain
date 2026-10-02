using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Core.Enforcement;

public static class BrainRoutes
{
    // The one place brain-scoped HTTP routes are rooted: /brains/{brainId} with route-id
    // validation and the membership filter applied once.
    public static RouteGroupBuilder Group(IEndpointRouteBuilder endpoints, string suffix = "")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("/brains/{brainId}" + suffix);
        group.AddEndpointFilter(async (context, next) =>
            await ValidateId(context.HttpContext) is { } invalid ? invalid : await next(context));
        group.AddEndpointFilter(BrainAccessFilter.EnforceAsync);
        return group;
    }

    internal static ValueTask<IResult?> ValidateId(HttpContext http)
        => ValueTask.FromResult<IResult?>(
            BrainScope.IsValidId(http.Request.RouteValues["brainId"] as string) ? null : Results.BadRequest());
}
