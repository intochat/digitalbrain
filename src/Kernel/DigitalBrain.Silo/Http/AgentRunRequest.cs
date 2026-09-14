using System.Text.Json;

namespace DigitalBrain.Kernel;

internal sealed record AgentRunRequest(string? ThreadId, string? RunId, bool Resume)
{
    // The AG-UI handler reads the body itself, so this buffers and rewinds it.
    internal static async Task<AgentRunRequest?> ReadAsync(HttpContext http)
    {
        if (!HttpMethods.IsPost(http.Request.Method))
        {
            return null;
        }

        http.Request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted).ConfigureAwait(false);
            var root = document.RootElement;
            var resume = root.TryGetProperty("forwardedProps", out var forwarded)
                && forwarded.ValueKind == JsonValueKind.Object
                && forwarded.TryGetProperty("resume", out var flag)
                && flag.ValueKind == JsonValueKind.True;
            return new AgentRunRequest(Text(root, "threadId"), Text(root, "runId"), resume);
        }
        catch (JsonException)
        {
            // Not an AG-UI run body; let the handler answer it.
            return null;
        }
        finally
        {
            http.Request.Body.Position = 0;
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
