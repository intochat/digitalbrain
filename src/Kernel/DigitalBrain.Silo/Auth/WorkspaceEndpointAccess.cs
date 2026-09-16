using DigitalBrain.Core;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed record WorkspaceEndpointMetadata;

internal static class WorkspaceEndpointAccess
{
    public static void AddWorkspaceEndpointAccess(this IServiceCollection services)
        => services.AddAuthorization(options => options.AddPolicy("WorkspaceOwner", policy => policy.RequireAssertion(context =>
            context.Resource is HttpContext http &&
            (!http.RequestServices.GetServices<ModuleEndpointListener>().Any(listener => listener.Port == http.Connection.LocalPort)
                || context.User.IsInRole("owner")))));

    public static RouteGroupBuilder MapOwnerWorkspace(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGroup("").WithMetadata(new ModuleEndpointMetadata(typeof(UIModule)),
                new PublicModuleEndpointMetadata(), new WorkspaceEndpointMetadata())
            .RequireAuthorization("WorkspaceOwner");
}
