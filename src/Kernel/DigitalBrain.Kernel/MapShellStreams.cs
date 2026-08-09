using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class SurfaceStreamsHttpMaps
{
    public static IEndpointRouteBuilder MapSurfaceStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.SurfaceEventsPath,
            static async Task (
                HttpContext http,
                string surfaceName,
                long? afterSequence,
                OwnerSessionJournal sessionJournal,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(surfaceName);
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(sessionJournal);
                cancellationToken.ThrowIfCancellationRequested();

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    WatchSurfaceOpenedAsync(sessionJournal, surfaceName, cursor, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    private static IAsyncEnumerable<SseItem<SurfaceOpenedEvent>> WatchSurfaceOpenedAsync(
        OwnerSessionJournal sessionJournal,
        string surfaceName,
        long afterSequence,
        CancellationToken cancellationToken)
        => JournalProjection.WatchAsync(
            token => sessionJournal.WatchSurfaceOutgoingAsync(surfaceName, afterSequence, token),
            HttpSurfacePaths.SurfaceOpenedEvent,
            ProjectSurfaceOpened,
            cancellationToken);

    private static SurfaceOpenedEvent? ProjectSurfaceOpened(SynapseDelivery delivery)
        => delivery.Synapse is not SurfaceOpened opened
            ? null
            : new SurfaceOpenedEvent(
                delivery.Sequence,
                opened.SurfaceKey,
                opened.Title,
                opened.CommandId.ToString(),
                opened.Surface.ToString());
}
