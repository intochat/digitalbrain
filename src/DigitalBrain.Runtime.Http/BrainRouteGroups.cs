using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Http;

/// <summary>
/// The one place brain-scoped HTTP routes are rooted: <c>/brains/{brainId}</c> (09-03; replaces the os <c>BrainRoutes</c>).
/// An invalid brain id is answered 400 in both groups.
/// </summary>
public static class BrainRouteGroups
{
    private const string MemberKey = "db.http.member";

    /// <summary>
    /// <c>/brains/{brainId}</c> plus <paramref name="suffix"/>, with the membership check: the caller must be authenticated
    /// (<see cref="SessionClaims.Principal"/> claim) and a member of the brain per identity@1 <see cref="IIdentity.Membership"/>
    /// (the keyed <c>identity@1</c> service). No identity@1, no principal or no membership is 403 (fail closed). The member is
    /// available to handlers through <see cref="MemberOf"/>.
    /// </summary>
    public static RouteGroupBuilder Brain(IEndpointRouteBuilder endpoints, string suffix = "")
    {
        var group = Session(endpoints, suffix);
        group.AddEndpointFilter(RequireMembership);
        return group;
    }

    /// <summary>
    /// <c>/brains/{brainId}</c> plus <paramref name="suffix"/> with the brain id check only, for edges that authenticate their own
    /// caller (for example a sandbox session token).
    /// </summary>
    public static RouteGroupBuilder Session(IEndpointRouteBuilder endpoints, string suffix = "")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(suffix);
        var group = endpoints.MapGroup("/brains/{brainId}" + suffix);
        group.AddEndpointFilter(async (context, next) =>
            BrainId.IsValid(context.HttpContext.Request.RouteValues["brainId"] as string) ? await next(context).ConfigureAwait(false) : Results.BadRequest());
        return group;
    }

    /// <summary>The membership <see cref="Brain"/> checked for this request, or null outside a brain group.</summary>
    public static Member? MemberOf(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items.TryGetValue(MemberKey, out var member) ? member as Member : null;
    }

    private static async ValueTask<object?> RequireMembership(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var brainId = http.Request.RouteValues["brainId"] as string;
        var principal = SessionPrincipal.PrincipalId(http.User);
        if (principal is null) { return Results.Unauthorized(); }
        var identity = http.RequestServices.GetKeyedService<IIdentity>(Capability.Identity.V1Id);
        if (identity is null || brainId is null) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        var member = await identity.Membership(brainId, principal, http.RequestAborted).ConfigureAwait(false);
        if (member is null) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        http.Items[MemberKey] = member;
        return await next(context).ConfigureAwait(false);
    }
}
