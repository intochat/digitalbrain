using System.Text.Json;

namespace DigitalBrain.Kernel;

internal sealed class AgentReplayResult(string threadId, string runId, string answer) : IResult
{
    private static readonly JsonSerializerOptions Frames = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        var messageId = Guid.NewGuid().ToString("N");
        await WriteAsync(response, new { type = "RUN_STARTED", threadId, runId }).ConfigureAwait(false);
        await WriteAsync(response, new { type = "TEXT_MESSAGE_START", messageId, role = "assistant" }).ConfigureAwait(false);
        await WriteAsync(response, new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = answer }).ConfigureAwait(false);
        await WriteAsync(response, new { type = "TEXT_MESSAGE_END", messageId }).ConfigureAwait(false);
        await WriteAsync(response, new { type = "RUN_FINISHED", threadId, runId }).ConfigureAwait(false);
    }

    private static async Task WriteAsync<TFrame>(HttpResponse response, TFrame frame)
    {
        await response.WriteAsync($"data: {JsonSerializer.Serialize(frame, Frames)}\n\n", response.HttpContext.RequestAborted).ConfigureAwait(false);
        await response.Body.FlushAsync(response.HttpContext.RequestAborted).ConfigureAwait(false);
    }
}
