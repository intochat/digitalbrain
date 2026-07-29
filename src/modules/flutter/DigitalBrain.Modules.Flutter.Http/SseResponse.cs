using System.Net.ServerSentEvents;
using System.Text.Json;

namespace DigitalBrain.UI;

internal static class SseResponse
{
    private static readonly byte[] ConnectedComment = ": connected\n\n"u8.ToArray();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync<T>(
        HttpResponse response,
        IAsyncEnumerable<SseItem<T>> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(events);

        response.Headers.CacheControl = UiHttpContract.CacheControlNoCache;
        response.ContentType = UiHttpContract.EventStreamContentType;
        await response.Body.WriteAsync(ConnectedComment, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);

        await SseFormatter.WriteAsync(
            events,
            response.Body,
            static (item, writer) =>
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(item.Data, Json);
                var span = writer.GetSpan(payload.Length);
                payload.CopyTo(span);
                writer.Advance(payload.Length);
            },
            cancellationToken);
    }
}
