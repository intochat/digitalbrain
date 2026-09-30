using DigitalBrain.AI;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Assistant;

internal static class AgentEndpoints
{
    private const int MaxVoiceBytes = 6 * 1024 * 1024;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ai/models", async (IDigitalBrain brain) =>
            Results.Ok(await brain.Get<IAssistant>(AssistantSurface.Key("catalog")).Models()));

        var brainRoutes = BrainRoutes.Group(endpoints);
        brainRoutes.MapGet("/conversations/{threadId}", async (string threadId, IDigitalBrain brain, CancellationToken ct) =>
            BrainScope.IsValidId(threadId)
                ? Results.Ok(await brain.Get<IAssistant>(AssistantSurface.Key(BrainScope.CurrentId())).ReadConversation(threadId, ct))
                : Results.BadRequest());
        brainRoutes.MapPost("/voice", async (VoiceInput input, IDigitalBrain brain, CancellationToken ct) =>
        {
            var result = await brain.Get<IAssistant>(AssistantSurface.Key(BrainScope.CurrentId())).Transcribe(input.Audio, ct);
            return result.Status == 200 ? Results.Ok(new { text = result.Text })
                : Results.Json(new { error = result.Error }, statusCode: result.Status);
        }).WithMetadata(new RequestSizeLimitAttribute(MaxVoiceBytes));

        endpoints.MapPost("/agent", async (AgentInput input, HttpContext http, IDigitalBrain brain) =>
        {
            if (!BrainScope.IsValidId(input.BrainId) || input.Messages is not { Count: 1 } || input.Messages[0].Role != "user")
            { http.Response.StatusCode = 400; return; }
            var (scope, denied) = await ResolveBodyBrain(http, input.BrainId);
            if (denied is not null) { await denied.ExecuteAsync(http); return; }
            var request = new AssistantRun(input.ThreadId, input.RunId, input.Messages[0].Content, scope!.Owner, input.ModelProfile);
            try
            {
                await foreach (var item in brain.Get<IAssistant>(AssistantSurface.Key(scope.Id)).Run(request, http.RequestAborted))
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
            catch (ProviderUnavailableException error) when (!http.Response.HasStarted)
            {
                await ProviderUnavailable(error).ExecuteAsync(http);
            }
            catch (InvalidOperationException error) when (!http.Response.HasStarted)
            {
                http.Response.StatusCode = StatusCodes.Status409Conflict;
                await http.Response.WriteAsJsonAsync(new { code = "RUN_CONFLICT", message = error.Message }, http.RequestAborted);
            }
        });
    }

    internal static IResult ProviderUnavailable(ProviderUnavailableException error)
        => Results.Json(new { integration = error.Integration, status = error.Status, missing = error.Missing }, statusCode: StatusCodes.Status409Conflict);

    internal static async Task<(BrainScope? Scope, IResult? Denied)> ResolveBodyBrain(HttpContext http, string brainId)
    {
        if (await BrainAccessFilter.Decide(http, brainId) is { } denied) { return (null, denied); }
        // The brain travels in the body; the route carries none, so resolve from the request, not the stamp.
        return (BrainScope.Create(CallerContextStamper.Require().AccountId, brainId), null);
    }

    internal sealed record AgentInput(string BrainId, string ThreadId, string RunId, IReadOnlyList<AgentMessage> Messages, string? ModelProfile = null);
    internal sealed record AgentMessage(string Role, string Content);
    internal sealed record VoiceInput(string? Audio);
}
