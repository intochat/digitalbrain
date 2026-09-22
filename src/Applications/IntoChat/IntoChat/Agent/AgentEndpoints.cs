using System.Text.Json;
using DigitalBrain.AI.Conversations;
using DigitalBrain.Contracts;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat.Agent;

internal static class AgentEndpoints
{
    public static void MapWorkspaceAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/workspaces/{workspaceId}/conversations/{threadId}", async (string workspaceId, string threadId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
        {
            if (!ValidId(workspaceId) || !ValidId(threadId)) { return Results.BadRequest(); }
            var scope = WorkspaceScope.Create(auth.Value.Username is { Length: > 0 } owner ? owner : BasicAuthGate.DefaultLogin, workspaceId);
            return Results.Ok(await brain.Get<IConversation>(ConversationCoordinator.Key(scope.Id, threadId)).Read().WaitAsync(ct));
        });
        routes.MapPost("/agent", async (AgentInput input, HttpContext http, ConversationCoordinator coordinator, IOptions<BasicAuthOptions> auth) =>
        {
            if (!ValidId(input.ThreadId) || !ValidId(input.RunId) || !ValidId(input.WorkspaceId)
                || input.Messages is not { Count: 1 } || input.Messages[0].Role != "user"
                || string.IsNullOrWhiteSpace(input.Messages[0].Content) || input.Messages[0].Content.Length > 32000)
            { http.Response.StatusCode = 400; return; }
            var scope = WorkspaceScope.Create(auth.Value.Username is { Length: > 0 } owner ? owner : BasicAuthGate.DefaultLogin, input.WorkspaceId);
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            async Task Emit(object value)
            {
                await http.Response.WriteAsync("data: " + JsonSerializer.Serialize(value) + "\n\n", http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
            }
            try { await coordinator.Run(scope.Id, input.ThreadId, input.RunId, input.Messages[0].Content, Emit, http.RequestAborted); }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { }
            catch (WorkspaceQueryException error)
            {
                if (!http.RequestAborted.IsCancellationRequested)
                { await Emit(new { type = "RUN_ERROR", message = "The table could not be opened: " + error.Message, code = "QUERY_INVALID" }); }
            }
            catch (Exception error)
            {
                http.RequestServices.GetRequiredService<ILogger<ConversationCoordinator>>().LogWarning(error, "Workspace agent run failed");
                if (!http.RequestAborted.IsCancellationRequested)
                { await Emit(new { type = "RUN_ERROR", message = "The request could not be completed. Check the data connection or try again.", code = "AGENT_FAILED" }); }
            }
        });
    }
    private static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(char.IsControl) && !value.Contains('/') && !value.Contains('\\');
    internal sealed record AgentInput(string WorkspaceId, string ThreadId, string RunId, IReadOnlyList<AgentMessage> Messages);
    internal sealed record AgentMessage(string Role, string Content);
}
