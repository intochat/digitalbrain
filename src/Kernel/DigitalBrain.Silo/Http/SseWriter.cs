using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Kernel;

internal static class SseWriter
{
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task StartAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        return WriteFrameAsync(response, ": connected\n\n", cancellationToken);
    }

    public static Task WriteAsync<T>(
        HttpResponse response, string eventName, T payload, long? id, CancellationToken cancellationToken)
        => WriteEventAsync(response, eventName, JsonSerializer.Serialize(payload, EventJson), id, cancellationToken);

    public static Task KeepAliveAsync(HttpResponse response, CancellationToken cancellationToken)
        => WriteEventAsync(response, "keepalive", "{}", null, cancellationToken);

    private static Task WriteEventAsync(
        HttpResponse response, string eventName, string json, long? id, CancellationToken cancellationToken)
    {
        var prefix = id is { } sequence ? $"id: {sequence.ToString(CultureInfo.InvariantCulture)}\n" : string.Empty;
        return WriteFrameAsync(response, $"{prefix}event: {eventName}\ndata: {json}\n\n", cancellationToken);
    }

    private static async Task WriteFrameAsync(HttpResponse response, string frame, CancellationToken cancellationToken)
    {
        await response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
