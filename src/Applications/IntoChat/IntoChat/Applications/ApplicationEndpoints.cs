using DigitalBrain.Identity.Configuration;
using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using DigitalBrain.CustomerResearcher;
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
    public static void MapApplications(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/workspaces/{workspaceId}/applications/customer-researcher/open", async (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            var key = CustomerResearcherSurface.Key(WorkspaceScope.Current(auth.Value, workspaceId).Id);
            var window = await brain.Get<ICustomerResearcher>(key).OpenWindow();
            return Results.Ok(new { id = window.Id, title = window.Title, kind = "surface", surface = window.Surface });
        }).AddEndpointFilter(BrainAccessFilter.EnforceAsync);
        var assistant = routes.MapGroup("/workspaces/{workspaceId}/applications/assistant")
            .AddEndpointFilter(BrainAccessFilter.EnforceAsync);
        assistant.MapPost("/start", async (string workspaceId, JsonElement input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            var appKey = AssistantSurface.Key(WorkspaceScope.Current(auth.Value, workspaceId).Id);
            await brain.Get<IAssistant>(appKey).Activate();
            if (input.ValueKind == JsonValueKind.Object && input.TryGetProperty("legacy", out var legacy))
            { await brain.Get<IAssistant>(appKey).RestoreLegacy(legacy.GetRawText()); }
            return Results.Ok(new { surface = new { kind = UIVocabulary.SurfaceType, name = UiParts.NameOf(appKey, "surface") } });
        });
        assistant.MapPost("/open", async (string workspaceId, OpenAssistantInput? input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            var key = AssistantSurface.Key(WorkspaceScope.Current(auth.Value, workspaceId).Id);
            var window = await brain.Get<IAssistant>(key).OpenWindow(input?.Draft, input?.Title);
            return Results.Ok(new { id = window.Id, title = window.Title, kind = "surface", surface = window.Surface });
        });
    }

    internal sealed record OpenAssistantInput(string? Draft = null, string? Title = null);
}
