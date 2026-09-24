using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using IntoChat.Apps.BuiltIn;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat.Apps;

internal static class BuiltInAppEndpoints
{
    public static void MapBuiltInApps(this IEndpointRouteBuilder routes)
    {
        var apps = routes.MapGroup("/workspaces/{workspaceId}/built-in")
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        apps.MapPost("/activate", async (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            await brain.Get<IAssistantApp>(WorkspaceScope.Current(auth.Value, workspaceId).Id).Activate();
            return Results.NoContent();
        });
        apps.MapPost("/{appId}/open", async (string workspaceId, string appId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            var key = WorkspaceScope.Current(auth.Value, workspaceId).Id;
            return appId switch
            {
                "settings" => Results.Ok(await brain.Get<ISettingsApp>(key).Activate()),
                "assistant" => Results.Ok(await brain.Get<IAssistantApp>(key).Activate()),
                _ => Results.NotFound(),
            };
        });
    }
}
