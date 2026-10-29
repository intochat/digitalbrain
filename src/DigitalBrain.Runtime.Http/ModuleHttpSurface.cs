using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Runtime.Http;

/// <summary>
/// A module's routes on the host HTTP edge (09-03, identity row 10). It replaces the os <c>IAuthorityHttpExtension</c> and
/// <c>BrainRoutes</c> pair. A module registers it with <see cref="ModuleHttpSurfaces.AddModuleHttpSurface{T}"/>; the host maps
/// every registered surface with one <see cref="ModuleHttpSurfaces.MapModuleHttpSurfaces"/> call.
/// </summary>
public interface IModuleHttpSurface
{
    /// <summary>
    /// Maps the module routes. <paramref name="brainGroup"/> is the host surface group: every route under it needs a session
    /// (an authenticated caller) unless it calls <c>.AllowAnonymous()</c> explicitly. Brain-scoped routes go under
    /// <see cref="BrainRouteGroups.Brain"/> (<c>/brains/{brainId}</c> with the membership check) or
    /// <see cref="BrainRouteGroups.Session"/> (brain id check only).
    /// </summary>
    void Map(IEndpointRouteBuilder brainGroup);
}

/// <summary>Registers and maps <see cref="IModuleHttpSurface"/>s.</summary>
public static class ModuleHttpSurfaces
{
    /// <summary>Registers <typeparamref name="T"/> as a module HTTP surface (once per type).</summary>
    public static IServiceCollection AddModuleHttpSurface<T>(this IServiceCollection services) where T : class, IModuleHttpSurface
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleHttpSurface, T>());
        return services;
    }

    /// <summary>
    /// Maps every registered surface onto one session-required group at the root of <paramref name="endpoints"/>: a request
    /// without an authenticated user is answered 401 unless the endpoint allows anonymous callers (<c>.AllowAnonymous()</c>).
    /// </summary>
    /// <returns>The surface group, for host conventions (rate limits, filters).</returns>
    public static RouteGroupBuilder MapModuleHttpSurfaces(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("");
        group.AddEndpointFilter(RequireSession);
        foreach (var surface in endpoints.ServiceProvider.GetServices<IModuleHttpSurface>()) { surface.Map(group); }
        return group;
    }

    private static async ValueTask<object?> RequireSession(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null || http.User.Identity?.IsAuthenticated == true)
        {
            return await next(context).ConfigureAwait(false);
        }

        return Results.Unauthorized();
    }
}
