using DigitalBrain.Identity.Configuration;
using DigitalBrain.Contracts;
using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using DigitalBrain.Flutter;
using DigitalBrain.Core.Enforcement;
using IntoChat.Apps.BuiltIn;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using DigitalBrain.Identity;

namespace IntoChat.Apps;

internal static class BuiltInAppEndpoints
{
    public static void MapBuiltInApps(this IEndpointRouteBuilder routes)
    {
        var apps = routes.MapGroup("/workspaces/{workspaceId}/built-in")
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        apps.MapPost("/activate", async (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            await brain.Get<IAssistant>(AssistantSurface.Key(WorkspaceScope.Current(auth.Value, workspaceId).Id)).Activate();
            return Results.NoContent();
        });
        apps.MapPost("/{appId}/open", async (string workspaceId, string appId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            var key = WorkspaceScope.Current(auth.Value, workspaceId).Id;
            if (appId == "assistant")
            {
                var window = await brain.Get<IAssistant>(AssistantSurface.Key(key)).OpenWindow();
                return Results.Ok(new { id = window.Id, title = window.Title, kind = "surface", surface = window.Surface });
            }
            return appId switch
            {
                "settings" => Results.Ok(await brain.Get<ISettingsApp>(key).Activate()),
                _ => Results.NotFound(),
            };
        });
    }
}
