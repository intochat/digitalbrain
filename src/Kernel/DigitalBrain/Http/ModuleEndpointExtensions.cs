using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

/// <summary>Identifies endpoint ownership; does not grant access.</summary>
public sealed record ModuleEndpointMetadata(Type ModuleType);

/// <summary>Explicitly permits a module endpoint on its public listener.</summary>
public sealed record PublicModuleEndpointMetadata;

/// <summary>A listener restricted to endpoints explicitly exposed by one module.</summary>
public sealed record ModuleEndpointListener(Type ModuleType, int Port)
{
    public IReadOnlyList<Type> AdditionalModuleTypes { get; init; } = [];
    public bool Exposes(Type moduleType) => moduleType == ModuleType || AdditionalModuleTypes.Contains(moduleType);
}

public static class ModuleEndpointExtensions
{
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (var module in endpoints.ServiceProvider.GetServices<IModule>())
        {
            module.Configure(endpoints);
        }

        return endpoints;
    }

    /// <summary>Apply after routing and before any host middleware that can answer requests.</summary>
    public static IApplicationBuilder UseModuleEndpointIsolation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var listeners = app.ApplicationServices.GetServices<ModuleEndpointListener>().ToArray();
        foreach (var listener in listeners)
        {
            if (listener.Port is < 1 or > 65535)
            {
                throw new InvalidOperationException("A module listener port must be between 1 and 65535.");
            }
        }

        if (listeners.Select(listener => listener.Port).Distinct().Count() != listeners.Length)
        {
            throw new InvalidOperationException("Each module listener must have a unique port.");
        }

        return app.Use(async (context, next) =>
        {
            var listener = listeners.SingleOrDefault(listener => listener.Port == context.Connection.LocalPort);
            var endpoint = context.GetEndpoint();
            if (listener is not null && (endpoint?.Metadata.GetMetadata<PublicModuleEndpointMetadata>() is null
                || endpoint.Metadata.GetMetadata<ModuleEndpointMetadata>() is not { } module
                || (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null && endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
                || !listener.Exposes(module.ModuleType)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
    }
}
