using DigitalBrain.Identity.Configuration;
using DigitalBrain.Assistant;
using DigitalBrain.Contracts;
using DigitalBrain.Identity;
using DigitalBrain.Core.Enforcement;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat.Agent;

internal static class VoiceEndpoints
{
    internal sealed record VoiceInput(string? Audio);

    public static void MapWorkspaceVoice(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/workspaces/{workspaceId}/voice", async (
            string workspaceId, VoiceInput input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth,
            CancellationToken ct) =>
        {
            if (!WorkspaceScope.IsValidId(workspaceId)) { return Results.BadRequest(); }
            var scope = WorkspaceScope.Current(auth.Value, workspaceId);
            var result = await brain.Get<IAssistant>(AssistantSurface.Key(scope.Id)).Transcribe(input.Audio, ct);
            return result.Status == 200 ? Results.Ok(new { text = result.Text })
                : Results.Json(new { error = result.Error }, statusCode: result.Status);
        }).AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(6 * 1024 * 1024));
    }
}
