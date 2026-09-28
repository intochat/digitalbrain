using DigitalBrain.Identity.Configuration;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Apps.Assistant;
using DigitalBrain.Identity;
using DigitalBrain.Core.Enforcement;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat.Agent;

internal static class AgentEndpoints
{
    public static string ConversationKey(string scope, string thread) => AssistantConversations.Key(scope, thread);

    public static void MapWorkspaceAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/ai/models", async (IDigitalBrain brain) =>
            Results.Ok(await brain.Get<IAssistant>(AssistantApp.Key("catalog")).Models()));
        routes.MapGet("/workspaces/{workspaceId}/conversations/{threadId}", async (string workspaceId, string threadId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
        {
            if (!WorkspaceScope.IsValidId(workspaceId) || !WorkspaceScope.IsValidId(threadId)) { return Results.BadRequest(); }
            var scope = WorkspaceScope.Current(auth.Value, workspaceId);
            return Results.Ok(await brain.Get<IAssistant>(AssistantApp.Key(scope.Id)).ReadConversation(threadId, ct));
        }).AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        routes.MapPost("/agent", async (AgentInput input, HttpContext http, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, IConfiguration configuration, IHostEnvironment environment) =>
        {
            if (!WorkspaceScope.IsValidId(input.WorkspaceId) || input.Messages is not { Count: 1 } || input.Messages[0].Role != "user")
            { http.Response.StatusCode = 400; return; }
            if (await WorkspaceAccessFilter.Decide(http, input.WorkspaceId) is { } denied) { await denied.ExecuteAsync(http); return; }
            var scope = WorkspaceScope.Current(auth.Value, input.WorkspaceId);
            var request = new AssistantRun(input.ThreadId, input.RunId, input.Messages[0].Content, scope.Owner, input.ModelProfile,
                ContentCapturePolicy.IsLocalOwner(auth.Value, configuration, environment), ContentCapturePolicy.ClassOf(input.Messages));
            try
            {
                await foreach (var item in brain.Get<IAssistant>(AssistantApp.Key(scope.Id)).Run(request, http.RequestAborted))
                {
                    if (!http.Response.HasStarted)
                    {
                        http.Response.ContentType = "text/event-stream";
                        http.Response.Headers.CacheControl = "no-cache";
                    }
                    await http.Response.WriteAsync("data: " + item + "\n\n", http.RequestAborted);
                    await http.Response.Body.FlushAsync(http.RequestAborted);
                }
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { }
            catch (ArgumentException error) when (!http.Response.HasStarted)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsJsonAsync(new { code = "MODEL_UNAVAILABLE", message = error.Message }, http.RequestAborted);
            }
            catch (InvalidOperationException error) when (!http.Response.HasStarted)
            {
                http.Response.StatusCode = StatusCodes.Status409Conflict;
                await http.Response.WriteAsJsonAsync(new { code = "RUN_CONFLICT", message = error.Message }, http.RequestAborted);
            }
        });
    }

    internal sealed record AgentInput(string WorkspaceId, string ThreadId, string RunId, IReadOnlyList<AgentMessage> Messages, string? ModelProfile = null);
    internal sealed record AgentMessage(string Role, string Content, string? Class = null);
}
