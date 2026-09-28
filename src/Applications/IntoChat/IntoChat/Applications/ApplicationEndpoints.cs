using DigitalBrain.Identity.Configuration;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Assistant;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using DigitalBrain.Identity;
using DigitalBrain.Contracts;
using System.Text.Json;

namespace IntoChat.Applications;

internal static class ApplicationEndpoints
{
    public static IServiceCollection AddApplications(this IServiceCollection services) => services
        .AddApplication(AppDefinition.Of<AssistantApp>());

    public static void MapApplications(this IEndpointRouteBuilder routes)
    {
        var assistant = routes.MapGroup("/workspaces/{workspaceId}/applications/assistant")
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        assistant.MapPost("/start", async (string workspaceId, JsonElement input, ApplicationCatalog catalog, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            var appKey = AssistantApp.Key(WorkspaceScope.Current(auth.Value, workspaceId).Id);
            await catalog.Start(AppDefinition.NameOf<AssistantApp>(), appKey);
            if (input.ValueKind == JsonValueKind.Object && input.TryGetProperty("legacy", out var legacy))
            { await brain.Get<IAssistant>(appKey).RestoreLegacy(legacy.GetRawText()); }
            return Results.Ok(new { surface = new { kind = UIVocabulary.SurfaceType, name = UiComposer.NameOf(appKey, "surface") } });
        });
        assistant.MapPost("/open", async (string workspaceId, OpenAssistantInput? input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            var key = AssistantApp.Key(WorkspaceScope.Current(auth.Value, workspaceId).Id);
            var window = await brain.Get<IAssistant>(key).OpenWindow(input?.Draft, input?.Title);
            return Results.Ok(new { id = window.Id, title = window.Title, kind = "surface", surface = window.Surface });
        });
    }

    internal sealed record OpenAssistantInput(string? Draft = null, string? Title = null);
}
