using DigitalBrain.Apps.Assistant;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using DigitalBrain.Identity;

namespace IntoChat.Applications;

internal static class ApplicationEndpoints
{
    public static IServiceCollection AddApplications(this IServiceCollection services) => services
        .AddSingleton(AppDefinition.Of<AssistantApp>())
        .AddSingleton<ApplicationCatalog>();

    public static void MapApplications(this IEndpointRouteBuilder routes)
    {
        var assistant = routes.MapGroup("/workspaces/{workspaceId}/applications/assistant")
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        assistant.MapPost("/start", async (string workspaceId, ApplicationCatalog catalog, IOptions<BasicAuthOptions> auth) =>
        {
            var appKey = WorkspaceScope.Current(auth.Value, workspaceId).Id + "/applications/assistant";
            await catalog.Start(AppDefinition.NameOf<AssistantApp>(), appKey);
            return Results.Ok(new { surface = new { kind = UIVocabulary.SurfaceType, name = UiComposer.NameOf(appKey, "surface") } });
        });
    }
}
