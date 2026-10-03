using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Assistant;

internal static class AssistantEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var assistant = BrainRoutes.Group(endpoints, "/applications/assistant");
        assistant.MapPost("/start", async (JsonElement input, IDigitalBrain brain) =>
        {
            var appKey = AssistantSurface.Key(BrainScope.CurrentId());
            await brain.Get<IAssistant>(appKey).Activate();
            return Results.Ok(new { surface = new { kind = UIVocabulary.SurfaceType, name = UiParts.NameOf(appKey, "surface") } });
        });
        assistant.MapPost("/open", async (OpenAssistantInput? input, IDigitalBrain brain) =>
        {
            var window = await brain.Get<IAssistant>(AssistantSurface.Key(BrainScope.CurrentId())).OpenWindow(input?.Draft, input?.Title);
            return Results.Ok(new { id = window.Id, title = window.Title, kind = "surface", surface = window.Surface });
        });

        var builtIn = BrainRoutes.Group(endpoints, "/built-in");
        builtIn.MapPost("/activate", async (IDigitalBrain brain) =>
        {
            await brain.Get<IAssistant>(AssistantSurface.Key(BrainScope.CurrentId())).Activate();
            return Results.NoContent();
        });
        builtIn.MapPost("/assistant/open", async (IDigitalBrain brain) =>
        {
            var window = await brain.Get<IAssistant>(AssistantSurface.Key(BrainScope.CurrentId())).OpenWindow();
            return Results.Ok(new { id = window.Id, title = window.Title, kind = "surface", surface = window.Surface });
        });
    }

    internal sealed record OpenAssistantInput(string? Draft = null, string? Title = null);
}
