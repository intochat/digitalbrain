using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

internal static class SseResponse
{
    private static readonly byte[] ConnectedComment = ": connected\n\n"u8.ToArray();
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions AiJson = CreateAiJson();

    public static async Task WriteAsync<T>(
        HttpResponse response,
        IAsyncEnumerable<SseItem<T>> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(events);

        response.Headers.CacheControl = HttpSurfacePaths.CacheControlNoCache;
        response.ContentType = HttpSurfacePaths.EventStreamContentType;
        await response.Body.WriteAsync(ConnectedComment, cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        var json = typeof(T) == typeof(ChatResponseUpdate) ? AiJson : EventJson;

        await SseFormatter.WriteAsync(
            events,
            response.Body,
            (item, writer) =>
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(item.Data, json);
                var span = writer.GetSpan(payload.Length);
                payload.CopyTo(span);
                writer.Advance(payload.Length);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static JsonSerializerOptions CreateAiJson()
        => new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };
}
